using System.Globalization;
using System.Text.Json;
using PlaceContext.App.Proxy;

namespace PlaceContext.App.Dashboard;

public sealed class DashboardHttpClient(EdgeHttpClient http) : IDashboardHttpClient
{
    public async Task<DashboardResponse> GetAsync(
        Guid? projectId,
        string callerToken,
        CancellationToken cancellationToken)
    {
        var reportsTask = http.GetAsync("Jobs", "api/jobs/observability", callerToken, cancellationToken);
        var projectsTask = http.GetAsync(
            "Projects", "api/projects/internal", callerToken, cancellationToken, useApiKey: true);
        var queuedTask = http.GetAsync(
            "Operations", "api/operations/internal/queued-count", callerToken, cancellationToken, useApiKey: true);
        await Task.WhenAll(reportsTask, projectsTask, queuedTask);

        var reports = (await reportsTask).PropertyOrEmptyArray("runs");
        var projects = await projectsTask;
        var project = projects.EnumerateArray().FirstOrDefault(item =>
            projectId is { } requested ? item.Guid("id") == requested : true);
        if (projectId is not null && project.ValueKind == JsonValueKind.Undefined)
            throw new EdgeHttpException(StatusCodes.Status404NotFound, "{\"error\":\"The selected project does not exist.\"}");

        var stats = BuildStats(reports, ReadQueued(await queuedTask));
        if (project.ValueKind == JsonValueKind.Undefined)
            return new DashboardResponse(null, stats, [], [], [], MapRuns(reports));

        var selectedId = project.Guid("id");
        var chainsTask = http.GetAsync("Jobs", $"api/jobs/projects/{selectedId}/chains", callerToken, cancellationToken);
        var jobsTask = http.GetAsync("Jobs", $"api/jobs/projects/{selectedId}", callerToken, cancellationToken);
        var dataTask = http.GetAsync("Data", $"api/v1/projects/{selectedId}/data-admin", callerToken, cancellationToken);
        var chartsTask = http.GetAsync(
            "Data", $"api/data/internal/projects/{selectedId}/charts", callerToken, cancellationToken, useApiKey: true);
        await Task.WhenAll(chainsTask, jobsTask, dataTask, chartsTask);

        var data = await dataTask;
        return new DashboardResponse(
            new DashboardProject(selectedId, project.String("name")),
            stats,
            MapChains(await chainsTask, await jobsTask),
            await MapEntitiesAsync(
                data.PropertyOrEmptyArray("entities"),
                data.PropertyOrEmptyArray("tables"),
                callerToken,
                cancellationToken),
            MapCharts(await chartsTask),
            MapRuns(reports));
    }

    public async Task<RunChainResponse> RunChainAsync(
        Guid projectId,
        Guid chainId,
        RunChainRequest? request,
        string callerToken,
        CancellationToken cancellationToken)
    {
        var chains = await http.GetAsync(
            "Jobs", $"api/jobs/projects/{projectId}/chains", callerToken, cancellationToken);
        var chain = chains.EnumerateArray().FirstOrDefault(item => item.Guid("id") == chainId);
        if (chain.ValueKind == JsonValueKind.Undefined)
            throw new EdgeHttpException(
                StatusCodes.Status404NotFound,
                "{\"error\":\"The selected chain does not exist in this project.\"}");

        var chainRunId = Guid.NewGuid();
        await http.PostAsync(
            "Jobs",
            $"api/jobs/internal/chains/{chainId}/runs",
            callerToken,
            new
            {
                projectId,
                inputPayload = request?.InputPayload,
                chainRunId,
                stepPayloadOverrides = request?.StepPayloadOverrides,
            },
            cancellationToken,
            useApiKey: true);
        return new RunChainResponse(
            chainRunId,
            $"Run of {chain.String("name")} started — follow it in the notifications bell.");
    }

    private async Task<IReadOnlyList<DashboardEntity>> MapEntitiesAsync(
        JsonElement entities,
        JsonElement tables,
        string callerToken,
        CancellationToken cancellationToken)
    {
        var entityList = entities.EnumerateArray().ToList();
        var chartTasks = entityList.Take(6).ToDictionary(
            entity => entity.Guid("id"),
            entity => BuildEntityChartAsync(entity, callerToken, cancellationToken));
        await Task.WhenAll(chartTasks.Values);

        return entityList.Select(entity =>
        {
            var tableName = entity.String("tableName");
            var table = tables.EnumerateArray().FirstOrDefault(item =>
                string.Equals(item.String("name"), tableName, StringComparison.OrdinalIgnoreCase));
            var chart = chartTasks.TryGetValue(entity.Guid("id"), out var task) ? task.Result : null;
            return new DashboardEntity(
                entity.Guid("id"),
                entity.Guid("projectId"),
                entity.String("name"),
                tableName,
                table.ValueKind == JsonValueKind.Undefined ? null : table.Int64("rowEstimate"),
                chart?.Column,
                chart?.Bars ?? []);
        }).ToList();
    }

    private async Task<EntityChartResult?> BuildEntityChartAsync(
        JsonElement entity,
        string callerToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = entity.Guid("projectId");
            var tableName = entity.String("tableName");
            var columns = await http.GetAsync(
                "Data",
                $"api/data/internal/projects/{projectId}/tables/{Uri.EscapeDataString(tableName)}/columns",
                callerToken,
                cancellationToken,
                useApiKey: true);
            var relationColumns = entity.PropertyOrEmptyArray("relations").EnumerateArray()
                .Select(relation => relation.String("column"));
            var textColumns = columns.EnumerateArray()
                .Where(column =>
                    (column.String("type").Contains("text", StringComparison.OrdinalIgnoreCase)
                     || column.String("type").Contains("char", StringComparison.OrdinalIgnoreCase))
                    && !string.Equals(
                        column.String("name"), entity.NullableString("labelColumn"), StringComparison.OrdinalIgnoreCase))
                .Select(column => column.String("name"));
            var column = relationColumns.Concat(textColumns).FirstOrDefault();
            if (column is null) return null;

            var safeColumn = column.Replace("\"", string.Empty, StringComparison.Ordinal);
            var safeTable = tableName.Replace("\"", string.Empty, StringComparison.Ordinal);
            var result = await http.PostAsync(
                "Data",
                $"api/v1/projects/{projectId}/data-studio/queries/run",
                callerToken,
                new
                {
                    sql = $"SELECT \"{safeColumn}\"::text, count(*) FROM \"{safeTable}\" GROUP BY 1 ORDER BY 2 DESC LIMIT 4",
                    source = "postgres",
                },
                cancellationToken);
            var rows = result.PropertyOrEmptyArray("rows").EnumerateArray().ToList();
            var counts = rows.Select(row =>
                long.TryParse(row.EnumerateArray().ElementAtOrDefault(1).GetString(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var count) ? count : 0).ToList();
            var maximum = counts.DefaultIfEmpty(0).Max();
            var bars = rows.Select((row, index) => new DashboardEntityBar(
                row.EnumerateArray().ElementAtOrDefault(0).GetString() ?? "—",
                counts[index],
                maximum > 0 ? (int)(counts[index] * 100 / maximum) : 0)).ToList();
            return bars.Count == 0 ? null : new EntityChartResult(column, bars);
        }
        catch (Exception exception) when (
            exception is EdgeHttpException or JsonException or InvalidOperationException
            && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static DashboardStats BuildStats(JsonElement reports, int queued)
    {
        var dayAgo = DateTimeOffset.UtcNow.AddHours(-24);
        var items = reports.EnumerateArray().ToList();
        return new DashboardStats(
            items.Count(report => report.Property("run").String("status") == "Running"),
            queued,
            items.Count(report => report.Property("run").String("status") == "Failed"
                                  && report.Property("run").Date("startedAt") >= dayAgo),
            items.Count(report => report.Property("run").String("status") is "Succeeded" or "Partial"
                                  && report.Property("run").Date("startedAt") >= dayAgo));
    }

    private static int ReadQueued(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Number
            ? payload.GetInt32()
            : payload.TryGetProperty("queued", out var queued) ? queued.GetInt32() : 0;

    private static IReadOnlyList<DashboardRun> MapRuns(JsonElement reports)
        => reports.EnumerateArray().Select(report =>
        {
            var run = report.Property("run");
            var shards = run.PropertyOrEmptyArray("shardResults").EnumerateArray().ToList();
            return new DashboardRun(
                run.Guid("id"),
                report.String("jobName"),
                report.String("projectName"),
                run.String("status"),
                shards.Count(shard => shard.String("outcome") == "Succeeded"),
                shards.Count(shard => shard.String("outcome") == "Failed"),
                run.Date("startedAt"),
                run.NullableDate("finishedAt"),
                run.Property("snapshot").String("mapSourceKind"));
        }).ToList();

    private static IReadOnlyList<DashboardChart> MapCharts(JsonElement charts)
    {
        var result = new List<DashboardChart>();
        foreach (var chart in charts.EnumerateArray().Where(item =>
                     item.String("tableName").StartsWith("sql:", StringComparison.Ordinal)
                     && item.String("html").TrimStart().StartsWith('{')))
        {
            try
            {
                using var document = JsonDocument.Parse(chart.String("html"));
                result.Add(new DashboardChart(
                    chart.String("tableName")["sql:".Length..],
                    document.RootElement.Clone(),
                    chart.Date("generatedAt")));
            }
            catch (JsonException) { }
        }
        return result;
    }

    private static IReadOnlyList<DashboardChain> MapChains(JsonElement chains, JsonElement jobs)
    {
        var jobsById = jobs.EnumerateArray().ToDictionary(job => job.Guid("id"));
        return chains.EnumerateArray().Select(chain =>
        {
            var stages = chain.PropertyOrEmptyArray("stages").EnumerateArray().ToList();
            var promptSteps = new List<DashboardChainStep>();
            var executionIndex = 0;
            foreach (var stage in stages)
            {
                if (stage.TryGetProperty("action", out var action) && action.ValueKind is not JsonValueKind.Null)
                {
                    executionIndex++;
                    continue;
                }
                foreach (var step in stage.PropertyOrEmptyArray("jobs").EnumerateArray())
                {
                    if (jobsById.TryGetValue(step.Guid("jobId"), out var job))
                    {
                        var parameters = job.PropertyOrEmptyArray("parameters").EnumerateArray().ToList();
                        if (parameters.Count > 0)
                        {
                            var defaults = ReadParameterDefaults(job.PropertyOrEmptyArray("inputPayloads"));
                            promptSteps.Add(new DashboardChainStep(
                                executionIndex,
                                job.String("name"),
                                parameters.Select(parameter => new DashboardParameter(
                                    parameter.String("name"),
                                    parameter.NullableString("label") ?? parameter.String("name"),
                                    parameter.Bool("required"),
                                    parameter.String("type"),
                                    parameter.PropertyOrEmptyArray("options").EnumerateArray()
                                        .Select(option => option.GetString() ?? string.Empty).ToList(),
                                    defaults.GetValueOrDefault(parameter.String("name"), string.Empty))).ToList()));
                        }
                    }
                    executionIndex++;
                }
            }
            return new DashboardChain(
                chain.Guid("id"), chain.Guid("projectId"), chain.String("name"),
                stages.Count, stages.Sum(stage => stage.PropertyOrEmptyArray("jobs").GetArrayLength()), promptSteps);
        }).ToList();
    }

    private static IReadOnlyDictionary<string, string> ReadParameterDefaults(JsonElement payloads)
    {
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var payload in payloads.EnumerateArray())
        {
            try
            {
                using var document = JsonDocument.Parse(payload.GetString() ?? string.Empty);
                if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
                foreach (var property in document.RootElement.EnumerateObject())
                    defaults.TryAdd(property.Name, property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Number => property.Value.GetRawText(),
                        _ => string.Empty,
                    });
            }
            catch (JsonException) { }
        }
        return defaults;
    }
}
