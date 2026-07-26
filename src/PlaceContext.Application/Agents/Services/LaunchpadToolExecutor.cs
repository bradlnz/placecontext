using System.Text;

namespace PlaceContext.Application.Agents.Services;

/// <summary>
/// Executes the server-safe launchpad tools (see <see cref="LaunchpadToolCatalog"/>) against the
/// application facade. Failure semantics mirror Chat.razor's Ok/Fail text protocol: this executor
/// never throws for tool-level failures — it returns the result text on success and an
/// <c>"Error: ..."</c> string on failure (bad args, unknown tool, or a caught exception).
/// Virtual so tests can substitute a recording fake.
/// </summary>
public class LaunchpadToolExecutor
{
    private readonly IPlaceContextService _svc;

    public LaunchpadToolExecutor(IPlaceContextService svc) => _svc = svc;

    public virtual async Task<string> ExecuteAsync(Guid projectId, string toolName, string args, CancellationToken ct)
    {
        try
        {
            return toolName switch
            {
                "list_tables" => await ListTablesAsync(projectId, ct),
                "query_table" => await QueryTableAsync(projectId, args, ct),
                "list_jobs" => await ListJobsAsync(projectId, ct),
                "list_job_runs" => await ListJobRunsAsync(args, ct),
                "list_chains" => await ListChainsAsync(projectId, ct),
                "run_job" => await RunJobAsync(args, ct),
                "run_job_chain" => await RunJobChainAsync(args, ct),
                "search" => await SearchAsync(projectId, args, ct),
                "query_graph" => await QueryGraphAsync(projectId, ct),
                "get_artifacts" => await GetArtifactsAsync(projectId, ct),
                "list_schedules" => await ListSchedulesAsync(projectId, args, ct),
                _ => $"Error: unknown tool '{toolName}'",
            };
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private async Task<string> ListTablesAsync(Guid projectId, CancellationToken ct)
    {
        var tables = await _svc.ListProjectDataTablesAsync(projectId, ct);
        var sb = new StringBuilder();
        sb.Append($"Project tables: {tables.Count}\n");
        foreach (var t in tables)
            sb.Append($"- {t.Name} ({t.RowEstimate} rows)\n");
        return sb.ToString();
    }

    private async Task<string> QueryTableAsync(Guid projectId, string args, CancellationToken ct)
    {
        var parts = args.Split('|');
        var tableName = parts.Length > 0 ? parts[0].Trim() : "";
        if (tableName.Length == 0)
            return "Error: usage: [[tool:query_table|tableName|page]]";
        var page = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var p) && p > 0 ? p : 1;
        var result = await _svc.QueryProjectTablePageAsync(projectId, tableName, null, page, 50, ct: ct);
        var sb = new StringBuilder();
        sb.Append($"Table: {tableName} ({result.TotalCount} rows)\n");
        sb.Append(string.Join(" | ", result.Columns));
        sb.Append("\n---\n");
        foreach (var row in result.Rows.Take(50))
        {
            sb.Append(string.Join(" | ", row.Select(v => v ?? "null")));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private async Task<string> ListJobsAsync(Guid projectId, CancellationToken ct)
    {
        var jobs = await _svc.ListJobsAsync(projectId, ct);
        var sb = new StringBuilder();
        sb.Append($"Jobs: {jobs.Count}\n");
        foreach (var j in jobs)
            sb.Append($"- {j.Name} (id: {j.Id})\n");
        return sb.ToString();
    }

    private async Task<string> ListJobRunsAsync(string args, CancellationToken ct)
    {
        if (!Guid.TryParse(args.Trim(), out var jobId) || jobId == Guid.Empty)
            return $"Error: invalid jobId '{args.Trim()}'";
        var runs = await _svc.ListJobRunsAsync(jobId, ct);
        var sb = new StringBuilder();
        sb.Append($"Job runs: {runs.Count}\n");
        foreach (var r in runs.Take(20))
            sb.Append($"- {r.Status} ({r.StartedAt:yyyy-MM-dd HH:mm})\n");
        return sb.ToString();
    }

    private async Task<string> ListChainsAsync(Guid projectId, CancellationToken ct)
    {
        var chains = await _svc.ListJobChainsAsync(projectId, ct);
        var sb = new StringBuilder();
        sb.Append($"Job chains: {chains.Count}\n");
        foreach (var c in chains)
            sb.Append($"- {c.Name} (id: {c.Id}, {c.Steps.Count} steps)\n");
        return sb.ToString();
    }

    private async Task<string> RunJobAsync(string args, CancellationToken ct)
    {
        if (!Guid.TryParse(args.Trim(), out var jobId) || jobId == Guid.Empty)
            return $"Error: invalid jobId '{args.Trim()}'";
        var run = await _svc.RunJobAsync(jobId, inputPayload: null, ct: ct);
        return $"Job run started: {run.Id}\nStatus: {run.Status}\nStarted: {run.StartedAt:yyyy-MM-dd HH:mm}";
    }

    private async Task<string> RunJobChainAsync(string args, CancellationToken ct)
    {
        // args: "chainId" or "chainId|payloadJson" — split only on the FIRST pipe so the
        // payload may itself contain pipes.
        var pipeIdx = args.IndexOf('|');
        var idText = (pipeIdx >= 0 ? args[..pipeIdx] : args).Trim();
        var payload = pipeIdx >= 0 ? args[(pipeIdx + 1)..].Trim() : null;
        if (string.IsNullOrEmpty(payload))
            payload = null;
        if (!Guid.TryParse(idText, out var chainId) || chainId == Guid.Empty)
            return $"Error: invalid chainId '{idText}'";

        var run = await _svc.RunJobChainAsync(chainId, payload, chainRunId: null, stepPayloadOverrides: null, ct: ct);
        var sb = new StringBuilder();
        sb.Append($"Chain run: {run.Id}\nStatus: {run.Status}\n");
        foreach (var step in run.Steps)
            sb.Append($"- {step.JobName}: {step.Status}\n");
        if (!string.IsNullOrEmpty(run.FinalOutput))
        {
            var output = run.FinalOutput.Length > 2000 ? run.FinalOutput[..2000] + "…" : run.FinalOutput;
            sb.Append($"\nFinal output:\n{output}");
        }
        return sb.ToString();
    }

    private async Task<string> SearchAsync(Guid projectId, string args, CancellationToken ct)
    {
        var query = args.Trim();
        if (query.Length == 0)
            return "Error: usage: [[tool:search|query]]";
        var matches = await _svc.SearchRunOutputsAsync(projectId, query, 8, ct);
        if (matches.Count == 0)
            return "No matching run outputs found. Semantic search may be disabled (no embedding API key configured).";
        var sb = new StringBuilder();
        sb.Append($"Matches for \"{query}\": {matches.Count}\n");
        foreach (var m in matches)
        {
            var snippet = m.Text.Length > 300 ? m.Text[..300] + "…" : m.Text;
            sb.Append($"- (score {m.Score:0.00}, run {m.JobRunId.ToString()[..8]}) {snippet}\n");
        }
        return sb.ToString();
    }

    private async Task<string> QueryGraphAsync(Guid projectId, CancellationToken ct)
    {
        var graph = await _svc.GetGraphVizAsync(projectId, ct);
        var sb = new StringBuilder();
        sb.AppendLine("# Project Dependency Graph");
        sb.AppendLine($"- **Nodes:** {graph.NodeCount}");
        sb.AppendLine($"- **Links:** {graph.LinkCount}");
        sb.AppendLine();

        var byKind = graph.Nodes.GroupBy(n => n.Kind ?? "unknown")
            .OrderByDescending(g => g.Count())
            .ToList();
        sb.AppendLine("## Entity types");
        foreach (var kind in byKind.Take(10))
            sb.AppendLine($"- **{kind.Key}:** {kind.Count()} nodes");
        sb.AppendLine();

        var hubs = graph.Nodes.Where(n => n.Degree >= 5).OrderByDescending(n => n.Degree).Take(10).ToList();
        if (hubs.Count > 0)
        {
            sb.AppendLine("## Key entities (hubs)");
            foreach (var n in hubs)
                sb.AppendLine($"- **{n.Label}** ({n.Degree} connections){(n.IsGod ? " ⭐" : "")}");
        }
        return sb.ToString();
    }

    private async Task<string> GetArtifactsAsync(Guid projectId, CancellationToken ct)
    {
        var artifacts = await _svc.ListProjectArtifactsAsync(projectId, 25, null, ct);
        if (artifacts.Count == 0)
            return "No artifacts found for this project yet.";
        var sb = new StringBuilder();
        sb.Append($"Artifacts: {artifacts.Count}\n");
        foreach (var a in artifacts)
            sb.Append($"- {a.Title} | {a.Kind} | {a.SizeBytes} | /runs/{a.RunId}/artifacts/{a.Id}\n");
        return sb.ToString();
    }

    private async Task<string> ListSchedulesAsync(Guid projectId, string args, CancellationToken ct)
    {
        var triggers = await _svc.ListTriggersAsync(projectId, ct);
        // Optional jobId filter, same as Chat.razor's list_schedules.
        if (Guid.TryParse(args.Trim(), out var jobId) && jobId != Guid.Empty)
            triggers = triggers.Where(t => t.JobId == jobId).ToList();
        var sb = new StringBuilder();
        sb.Append($"Schedules: {triggers.Count}\n");
        foreach (var t in triggers)
        {
            sb.Append($"- {t.Name} ({t.Kind})\n");
            if (t.Kind == "Schedule")
                sb.Append($"  Cron: {t.CronExpression} | Next: {t.NextRunAt?.ToString("yyyy-MM-dd HH:mm") ?? "—"}\n");
            sb.Append($"  Enabled: {t.Enabled} | Last fired: {t.LastFiredAt?.ToString("yyyy-MM-dd HH:mm") ?? "never"}\n");
        }
        return sb.ToString();
    }
}
