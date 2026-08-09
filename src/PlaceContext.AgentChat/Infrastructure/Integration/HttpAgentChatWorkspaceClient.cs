using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.AgentChat.Integration;
using PlaceContext.Application.Agents;

namespace PlaceContext.AgentChat.Infrastructure.Integration;

public sealed class HttpAgentChatWorkspaceClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IAgentChatWorkspaceClient
{
    public async Task<string> BuildContextAsync(
        Guid projectId,
        string userMessage,
        int maxChunks,
        CancellationToken ct = default)
    {
        var output = new StringBuilder();
        var encoded = Uri.EscapeDataString(userMessage);

        try
        {
            var matches = await GetAsync(
                "Search",
                $"api/search/internal/projects/{projectId}/run-outputs?term={encoded}&take={Math.Clamp(maxChunks, 1, 50)}",
                ct);
            if (matches.ValueKind == JsonValueKind.Array && matches.GetArrayLength() > 0)
            {
                output.AppendLine("## Recent run outputs (semantically relevant)");
                foreach (var match in matches.EnumerateArray())
                {
                    var runId = Text(match, "jobRunId");
                    var text = Text(match, "text");
                    output.AppendLine($"- run {Short(runId)}: {Truncate(text, 500)}");
                }
                output.AppendLine();
            }
        }
        catch { }

        try
        {
            var graph = await GetAsync("Data", $"api/data/internal/projects/{projectId}/graph", ct);
            if (graph.TryGetProperty("nodes", out var nodes)
                && nodes.ValueKind == JsonValueKind.Array
                && nodes.GetArrayLength() > 0)
            {
                output.AppendLine("## Project dependency graph");
                foreach (var node in nodes.EnumerateArray()
                             .OrderByDescending(node => Number(node, "degree"))
                             .Take(12))
                    output.AppendLine($"- {Text(node, "label")} (degree: {Number(node, "degree")})");
                output.AppendLine();
            }
        }
        catch { }

        try
        {
            var artifacts = await GetAsync(
                "Artifacts",
                $"api/artifacts/internal/projects/{projectId}?take={Math.Clamp(maxChunks, 1, 50)}&search={encoded}",
                ct);
            if (artifacts.ValueKind == JsonValueKind.Array && artifacts.GetArrayLength() > 0)
            {
                output.AppendLine("## Related artifacts");
                foreach (var artifact in artifacts.EnumerateArray())
                    output.AppendLine($"- {Text(artifact, "title")} ({Text(artifact, "kind")}) — id:{Text(artifact, "id")}");
                output.AppendLine();
            }
        }
        catch { }

        return output.ToString();
    }

    public Task<string> ExecuteToolAsync(
        Guid projectId,
        string toolName,
        string args,
        CancellationToken ct = default)
        => toolName switch
        {
            AgentToolNames.ListTables => ListTablesAsync(projectId, ct),
            AgentToolNames.QueryTable => QueryTableAsync(projectId, args, ct),
            AgentToolNames.ListJobs => ListJobsAsync(projectId, ct),
            AgentToolNames.ListJobRuns => ListJobRunsAsync(projectId, args, ct),
            AgentToolNames.ListChains => ListChainsAsync(projectId, ct),
            AgentToolNames.RunJob => RunJobAsync(projectId, args, ct),
            AgentToolNames.RunJobChain => RunJobChainAsync(projectId, args, ct),
            AgentToolNames.Search => SearchAsync(projectId, args, ct),
            AgentToolNames.QueryGraph => QueryGraphAsync(projectId, ct),
            AgentToolNames.GetArtifacts => GetArtifactsAsync(projectId, ct),
            AgentToolNames.ListSchedules => ListSchedulesAsync(projectId, args, ct),
            _ => Task.FromResult($"Error: unknown tool '{toolName}'"),
        };

    public async Task<AgentChatTablePage> QueryTablePageAsync(
        Guid projectId,
        string tableName,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var path = $"api/data/internal/projects/{projectId}/rows/tables/{Uri.EscapeDataString(tableName)}/page"
            + $"?page={Math.Max(page, 1)}&pageSize={Math.Clamp(pageSize, 1, 500)}";
        if (!string.IsNullOrWhiteSpace(search))
            path += "&search=" + Uri.EscapeDataString(search);
        using var response = await SendAsync("Data", HttpMethod.Get, path, null, ct);
        return await response.Content.ReadFromJsonAsync<AgentChatTablePage>(ct)
            ?? new AgentChatTablePage([], [], 0);
    }

    private async Task<string> ListTablesAsync(Guid projectId, CancellationToken ct)
    {
        var tables = await GetAsync("Data", $"api/data/internal/projects/{projectId}/rows/tables", ct);
        var values = tables.ValueKind == JsonValueKind.Array ? tables.EnumerateArray().ToArray() : [];
        var output = new StringBuilder($"Project tables: {values.Length}\n");
        foreach (var table in values)
            output.AppendLine($"- {Text(table, "name")} ({Number(table, "rowEstimate")} rows)");
        return output.ToString();
    }

    private async Task<string> QueryTableAsync(Guid projectId, string args, CancellationToken ct)
    {
        var parts = args.Split('|');
        var tableName = parts.Length > 0 ? parts[0].Trim() : string.Empty;
        if (tableName.Length == 0) return "Error: usage: [[tool:query_table|tableName|page]]";
        var pageNumber = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var parsed) && parsed > 0
            ? parsed
            : 1;
        var page = await QueryTablePageAsync(projectId, tableName, null, pageNumber, 50, ct);
        var output = new StringBuilder($"Table: {tableName} ({page.TotalCount} rows)\n");
        output.AppendLine(string.Join(" | ", page.Columns));
        output.AppendLine("---");
        foreach (var row in page.Rows.Take(50)) output.AppendLine(string.Join(" | ", row.Select(value => value ?? "null")));
        return output.ToString();
    }

    private async Task<string> ListJobsAsync(Guid projectId, CancellationToken ct)
    {
        var catalog = await CatalogAsync(projectId, ct);
        var jobs = Array(catalog, "jobs");
        var output = new StringBuilder($"Jobs: {jobs.Count}\n");
        foreach (var job in jobs) output.AppendLine($"- {Text(job, "name")} (id: {Text(job, "id")})");
        return output.ToString();
    }

    private async Task<string> ListJobRunsAsync(Guid projectId, string args, CancellationToken ct)
    {
        if (!Guid.TryParse(args.Trim(), out var jobId) || jobId == Guid.Empty)
            return $"Error: invalid jobId '{args.Trim()}'";
        var runs = Array(await CatalogAsync(projectId, ct), "runs")
            .Where(run => string.Equals(Text(run, "jobId"), jobId.ToString(), StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();
        var output = new StringBuilder($"Job runs: {runs.Count}\n");
        foreach (var run in runs) output.AppendLine($"- {Text(run, "status")} ({Text(run, "startedAt")})");
        return output.ToString();
    }

    private async Task<string> ListChainsAsync(Guid projectId, CancellationToken ct)
    {
        var chains = Array(await CatalogAsync(projectId, ct), "chains");
        var output = new StringBuilder($"Job chains: {chains.Count}\n");
        foreach (var chain in chains)
            output.AppendLine($"- {Text(chain, "name")} (id: {Text(chain, "id")}, {Number(chain, "stepCount")} steps)");
        return output.ToString();
    }

    private async Task<string> RunJobAsync(Guid projectId, string args, CancellationToken ct)
    {
        if (!Guid.TryParse(args.Trim(), out var jobId) || jobId == Guid.Empty)
            return $"Error: invalid jobId '{args.Trim()}'";
        var run = await PostAsync("Jobs", $"api/jobs/internal/jobs/{jobId}/runs", new { projectId }, ct);
        return $"Job run started: {Text(run, "id")}\nStatus: {Text(run, "status")}\nStarted: {Text(run, "startedAt")}";
    }

    private async Task<string> RunJobChainAsync(Guid projectId, string args, CancellationToken ct)
    {
        var separator = args.IndexOf('|');
        var key = (separator >= 0 ? args[..separator] : args).Trim();
        var payload = separator >= 0 ? args[(separator + 1)..].Trim() : null;
        var catalog = await CatalogAsync(projectId, ct);
        var chains = Array(catalog, "chains");
        JsonElement chain;
        if (Guid.TryParse(key, out var chainId) && chainId != Guid.Empty)
            chain = chains.FirstOrDefault(value => string.Equals(Text(value, "id"), chainId.ToString(), StringComparison.OrdinalIgnoreCase));
        else
            chain = chains.FirstOrDefault(value => string.Equals(Text(value, "name"), key, StringComparison.OrdinalIgnoreCase));
        if (chain.ValueKind == JsonValueKind.Undefined)
            return $"Error: unknown chain '{key}'.";
        chainId = Guid.Parse(Text(chain, "id"));
        var run = await PostAsync(
            "Jobs",
            $"api/jobs/internal/chains/{chainId}/runs",
            new { projectId, inputPayload = string.IsNullOrWhiteSpace(payload) ? null : payload },
            ct);
        var output = new StringBuilder($"Chain run: {Text(run, "id")}\nStatus: {Text(run, "status")}\n");
        foreach (var step in Array(run, "steps"))
            output.AppendLine($"- {Text(step, "jobName")}: {Text(step, "status")}");
        var finalOutput = Text(run, "finalOutput");
        if (!string.IsNullOrWhiteSpace(finalOutput)) output.AppendLine("\nFinal output:\n" + Truncate(finalOutput, 2000));
        return output.ToString();
    }

    private async Task<string> SearchAsync(Guid projectId, string args, CancellationToken ct)
    {
        var query = args.Trim();
        if (query.Length == 0) return "Error: usage: [[tool:search|query]]";
        var matches = await GetAsync(
            "Search",
            $"api/search/internal/projects/{projectId}/run-outputs?term={Uri.EscapeDataString(query)}&take=8",
            ct);
        var values = matches.ValueKind == JsonValueKind.Array ? matches.EnumerateArray().ToArray() : [];
        if (values.Length == 0) return "No matching run outputs found.";
        var output = new StringBuilder($"Matches for \"{query}\": {values.Length}\n");
        foreach (var match in values)
            output.AppendLine($"- (score {Text(match, "score")}, run {Short(Text(match, "jobRunId"))}) {Truncate(Text(match, "text"), 300)}");
        return output.ToString();
    }

    private async Task<string> QueryGraphAsync(Guid projectId, CancellationToken ct)
    {
        var graph = await GetAsync("Data", $"api/data/internal/projects/{projectId}/graph", ct);
        var nodes = Array(graph, "nodes");
        var links = Array(graph, "links");
        var output = new StringBuilder("# Project Dependency Graph\n");
        output.AppendLine($"- **Nodes:** {nodes.Count}");
        output.AppendLine($"- **Links:** {links.Count}");
        foreach (var node in nodes.OrderByDescending(node => Number(node, "degree")).Take(10))
            output.AppendLine($"- **{Text(node, "label")}** ({Number(node, "degree")} connections)");
        return output.ToString();
    }

    private async Task<string> GetArtifactsAsync(Guid projectId, CancellationToken ct)
    {
        var artifacts = await GetAsync("Artifacts", $"api/artifacts/internal/projects/{projectId}?take=25", ct);
        var values = artifacts.ValueKind == JsonValueKind.Array ? artifacts.EnumerateArray().ToArray() : [];
        if (values.Length == 0) return "No artifacts found for this project yet.";
        var output = new StringBuilder($"Artifacts: {values.Length}\n");
        foreach (var artifact in values)
            output.AppendLine($"- {Text(artifact, "title")} | {Text(artifact, "kind")} | {Number(artifact, "sizeBytes")} | /runs/{Text(artifact, "runId")}/artifacts/{Text(artifact, "id")}");
        return output.ToString();
    }

    private async Task<string> ListSchedulesAsync(Guid projectId, string args, CancellationToken ct)
    {
        var triggers = Array(await CatalogAsync(projectId, ct), "triggers");
        if (Guid.TryParse(args.Trim(), out var jobId) && jobId != Guid.Empty)
            triggers = triggers.Where(trigger => string.Equals(Text(trigger, "jobId"), jobId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
        var output = new StringBuilder($"Schedules: {triggers.Count}\n");
        foreach (var trigger in triggers)
            output.AppendLine($"- {Text(trigger, "name")} ({Text(trigger, "kind")}) — enabled: {Text(trigger, "enabled")}");
        return output.ToString();
    }

    private Task<JsonElement> CatalogAsync(Guid projectId, CancellationToken ct)
        => GetAsync("Jobs", $"api/jobs/internal/projects/{projectId}/catalog", ct);

    private async Task<JsonElement> GetAsync(string service, string path, CancellationToken ct)
    {
        using var response = await SendAsync(service, HttpMethod.Get, path, null, ct);
        return await ReadJsonAsync(response, ct);
    }

    private async Task<JsonElement> PostAsync(string service, string path, object payload, CancellationToken ct)
    {
        using var response = await SendAsync(service, HttpMethod.Post, path, payload, ct);
        return await ReadJsonAsync(response, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(
        string service,
        HttpMethod method,
        string path,
        object? payload,
        CancellationToken ct)
    {
        var origin = configuration[$"PlaceContext:AgentChat:{service}:BaseAddress"]
            ?? configuration[$"PlaceContext:Microservices:Destinations:{service}"]
            ?? throw new InvalidOperationException($"Configure the {service} service destination for AgentChat.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(method, new Uri(new Uri(baseAddress), path));
        request.Headers.Add("X-Api-Key", apiKey);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        try
        {
            response.EnsureSuccessStatusCode();
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return document.RootElement.Clone();
    }

    private static List<JsonElement> Array(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.Clone()).ToList()
            : [];

    private static string Text(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property)) return string.Empty;
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            _ => property.ToString(),
        };
    }

    private static long Number(JsonElement value, string name)
        => value.TryGetProperty(name, out var property) && property.TryGetInt64(out var number)
            ? number
            : 0;

    private static string Short(string value)
        => value.Length <= 8 ? value : value[..8];

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "…";
}
