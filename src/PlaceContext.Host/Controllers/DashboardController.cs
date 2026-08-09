using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.App.Dashboard;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Operations;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Dashboard resources in the canonical PlaceContext API. It composes application read models
/// concurrently and exposes a stable versioned contract to authenticated clients.
/// </summary>
[ApiController]
[Route("api/v1/dashboard")]
[Authorize(
    AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme,
    Policy = Permission.ProjectsView)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class DashboardController(
    IPlaceContextService placeContextService,
    ICurrentTenant currentTenant,
    OperationCenter operationCenter) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get(
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken)
    {
        var reportsTask = placeContextService.ListRecentRunReportsAsync(50, cancellationToken);
        var projectsTask = placeContextService.GetProjectsAsync(cancellationToken);
        await Task.WhenAll(reportsTask, projectsTask);

        var reports = await reportsTask;
        var projects = await projectsTask;
        var project = projectId is { } requestedProjectId
            ? projects.FirstOrDefault(item => item.Id == requestedProjectId)
            : projects.FirstOrDefault();

        if (projectId is not null && project is null)
            return NotFound(new { error = "The selected project does not exist." });

        var stats = BuildStats(reports);
        if (project is null)
        {
            return Ok(new DashboardResponse(
                null,
                stats,
                [],
                [],
                [],
                MapRuns(reports)));
        }

        var chainsTask = placeContextService.ListJobChainsAsync(project.Id, cancellationToken);
        var jobsTask = placeContextService.ListJobsAsync(project.Id, cancellationToken);
        var chartsTask = placeContextService.ListProjectChartsAsync(project.Id, cancellationToken);
        var entitiesTask = placeContextService.ListDataEntitiesAsync(project.Id, cancellationToken);
        var tablesTask = placeContextService.ListProjectDataTablesAsync(project.Id, cancellationToken);

        await Task.WhenAll(chainsTask, jobsTask, chartsTask, entitiesTask, tablesTask);

        var jobs = await jobsTask;
        var entities = await entitiesTask;
        var tables = await tablesTask;
        var entityResponses = await MapEntitiesAsync(
            entities,
            tables,
            placeContextService,
            cancellationToken);

        return Ok(new DashboardResponse(
            new DashboardProject(project.Id, project.Name),
            stats,
            MapChains(await chainsTask, jobs),
            entityResponses,
            MapCharts(await chartsTask),
            MapRuns(reports)));
    }

    [HttpPost("projects/{projectId:guid}/chains/{chainId:guid}/runs")]
    [Authorize(Policy = Permission.JobsRun)]
    public async Task<ActionResult<RunChainResponse>> RunChain(
        Guid projectId,
        Guid chainId,
        [FromBody] RunChainRequest? request,
        CancellationToken cancellationToken)
    {
        var chain = (await placeContextService.ListJobChainsAsync(projectId, cancellationToken))
            .FirstOrDefault(item => item.Id == chainId);
        if (chain is null)
            return NotFound(new { error = "The selected chain does not exist in this project." });

        var tenant = CurrentTenant.Current;
        if (tenant is null)
            return Unauthorized(new { error = "No tenant is resolved for this session." });

        var chainRunId = Guid.NewGuid();
        operationCenter.Run(
            tenant,
            projectId,
            $"Run chain — {chain.Name}",
            $"/project/{projectId}/chains",
            async (services, operationCancellationToken) =>
            {
                var service = services.GetRequiredService<IPlaceContextService>();
                var result = await service.RunJobChainAsync(
                    chainId,
                    request?.InputPayload,
                    chainRunId,
                    request?.StepPayloadOverrides,
                    operationCancellationToken);
                return $"chain finished — {result.Status}";
            });

        return Accepted(new RunChainResponse(
            chainRunId,
            $"Run of {chain.Name} started — follow it in the notifications bell."));
    }

    private DashboardStats BuildStats(IReadOnlyList<RunReportView> reports)
    {
        var dayAgo = DateTimeOffset.UtcNow.AddHours(-24);
        var queued = currentTenant.IsResolved
            ? operationCenter.ListForTenant(currentTenant.TenantId)
                .Count(operation => operation.Status == PortalOperationStatus.Queued)
            : 0;

        return new DashboardStats(
            reports.Count(report => report.Run.Status == "Running"),
            queued,
            reports.Count(report =>
                report.Run.Status == "Failed" && report.Run.StartedAt >= dayAgo),
            reports.Count(report =>
                report.Run.Status is "Succeeded" or "Partial"
                && report.Run.StartedAt >= dayAgo));
    }

    private static IReadOnlyList<DashboardChain> MapChains(
        IReadOnlyList<JobChainView> chains,
        IReadOnlyList<JobView> jobs)
    {
        var jobsById = jobs.ToDictionary(job => job.Id);
        return chains.Select(chain =>
        {
            var promptSteps = new List<DashboardChainStep>();
            var executionIndex = 0;
            foreach (var stage in chain.Stages)
            {
                if (stage.Action is not null)
                {
                    executionIndex++;
                    continue;
                }

                foreach (var step in stage.Jobs)
                {
                    if (jobsById.TryGetValue(step.JobId, out var job) && job.Parameters.Count > 0)
                    {
                        var defaults = ReadParameterDefaults(job.InputPayloads);
                        promptSteps.Add(new DashboardChainStep(
                            executionIndex,
                            job.Name,
                            job.Parameters.Select(parameter => new DashboardParameter(
                                parameter.Name,
                                parameter.Label ?? parameter.Name,
                                parameter.Required,
                                parameter.Type,
                                parameter.Options ?? [],
                                defaults.GetValueOrDefault(parameter.Name, string.Empty)))
                            .ToList()));
                    }

                    executionIndex++;
                }
            }

            return new DashboardChain(
                chain.Id,
                chain.ProjectId,
                chain.Name,
                chain.Stages.Count,
                chain.Steps.Count,
                promptSteps);
        }).ToList();
    }

    private static IReadOnlyDictionary<string, string> ReadParameterDefaults(
        IReadOnlyList<string> inputPayloads)
    {
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var payload in inputPayloads)
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!defaults.ContainsKey(property.Name))
                    {
                        defaults[property.Name] = property.Value.ValueKind switch
                        {
                            JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                            JsonValueKind.True => "true",
                            JsonValueKind.False => "false",
                            JsonValueKind.Number => property.Value.GetRawText(),
                            _ => string.Empty,
                        };
                    }
                }
            }
            catch (JsonException)
            {
                // Stored shard payloads may be plain text; only JSON objects can prefill fields.
            }
        }

        return defaults;
    }

    private static IReadOnlyList<DashboardChart> MapCharts(
        IReadOnlyList<ProjectChartView> charts)
    {
        var result = new List<DashboardChart>();
        foreach (var chart in charts.Where(item =>
                     item.TableName.StartsWith("sql:", StringComparison.Ordinal)
                     && item.Html.TrimStart().StartsWith('{')))
        {
            try
            {
                using var document = JsonDocument.Parse(chart.Html);
                result.Add(new DashboardChart(
                    chart.TableName["sql:".Length..],
                    document.RootElement.Clone(),
                    chart.GeneratedAt));
            }
            catch (JsonException)
            {
                // A malformed stored chart should not prevent the rest of the Dashboard loading.
            }
        }

        return result;
    }

    private static IReadOnlyList<DashboardRun> MapRuns(
        IReadOnlyList<RunReportView> reports)
        => reports.Select(report => new DashboardRun(
            report.Run.Id,
            report.JobName,
            report.ProjectName,
            report.Run.Status,
            report.Run.ShardResults.Count(shard => shard.Outcome == "Succeeded"),
            report.Run.ShardResults.Count(shard => shard.Outcome == "Failed"),
            report.Run.StartedAt,
            report.Run.FinishedAt,
            report.Run.Snapshot.MapSourceKind)).ToList();

    private static async Task<IReadOnlyList<DashboardEntity>> MapEntitiesAsync(
        IReadOnlyList<DataEntityView> entities,
        IReadOnlyList<ProjectTableInfo> tables,
        IPlaceContextService service,
        CancellationToken cancellationToken)
    {
        var chartTasks = entities.Take(6).ToDictionary(
            entity => entity.Id,
            entity => BuildEntityChartAsync(entity, service, cancellationToken));

        await Task.WhenAll(chartTasks.Values);

        return entities.Select(entity =>
        {
            var table = tables.FirstOrDefault(item => string.Equals(
                item.Name,
                entity.TableName,
                StringComparison.OrdinalIgnoreCase));
            var chart = chartTasks.TryGetValue(entity.Id, out var chartTask)
                ? chartTask.Result
                : null;

            return new DashboardEntity(
                entity.Id,
                entity.ProjectId,
                entity.Name,
                entity.TableName,
                table?.RowEstimate,
                chart?.Column,
                chart?.Bars ?? []);
        }).ToList();
    }

    private static async Task<EntityChartResult?> BuildEntityChartAsync(
        DataEntityView entity,
        IPlaceContextService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var columns = await service.ListProjectTableColumnsAsync(
                entity.ProjectId,
                entity.TableName,
                cancellationToken);
            var column = entity.Relations.Select(relation => relation.Column)
                .Concat(columns.Where(item =>
                        (item.Type.Contains("text", StringComparison.OrdinalIgnoreCase)
                         || item.Type.Contains("char", StringComparison.OrdinalIgnoreCase))
                        && !string.Equals(
                            item.Name,
                            entity.LabelColumn,
                            StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Name))
                .FirstOrDefault();
            if (column is null)
                return null;

            var safeColumn = column.Replace("\"", string.Empty, StringComparison.Ordinal);
            var safeTable = entity.TableName.Replace("\"", string.Empty, StringComparison.Ordinal);
            var result = await service.ExecuteProjectDataAsync(
                entity.ProjectId,
                $"SELECT \"{safeColumn}\"::text, count(*) FROM \"{safeTable}\" GROUP BY 1 ORDER BY 2 DESC LIMIT 4",
                cancellationToken);
            var counts = result.Rows.Select(row =>
                long.TryParse(row.ElementAtOrDefault(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                    ? count
                    : 0).ToList();
            var maximum = counts.DefaultIfEmpty(0).Max();
            var bars = result.Rows.Select((row, index) => new DashboardEntityBar(
                row.ElementAtOrDefault(0) ?? "—",
                counts[index],
                maximum > 0 ? (int)(counts[index] * 100 / maximum) : 0)).ToList();

            return bars.Count == 0 ? null : new EntityChartResult(column, bars);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

}
