using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Chat;
using PlaceContext.Infrastructure.Caching;
using PlaceContext.Application.Agents;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ChatViewModel
{
    // ── Tool execution ───────────────────────────────────────────────────────

    private async Task<ToolCallResult> ExecuteToolAsync(string toolName, string args, CancellationToken ct)
    {
        if (!ProjectId.HasValue) return ToolCallResult.Fail(ChatCopy.NoProjectSelected);
        try
        {
            return toolName switch
            {
                AgentToolNames.QueryTable => await ExecuteQueryTableAsync(args, ct),
                AgentToolNames.ListTables => await ExecuteListTablesAsync(ct),
                AgentToolNames.ListJobs => await ExecuteListJobsAsync(ct),
                AgentToolNames.ListJobRuns => await ExecuteListJobRunsAsync(args, ct),
                AgentToolNames.ListChains => await ExecuteListChainsAsync(ct),
                AgentToolNames.RenderGraph => await ExecuteRenderGraphAsync(args, ct),
                AgentToolNames.QueryGraph => await ExecuteQueryGraphAsync(ct),
                AgentToolNames.Search => await ExecuteSearchAsync(args, ct),
                AgentToolNames.GetArtifacts => await ExecuteGetArtifactsAsync(args, ct),
                AgentToolNames.ShowArtifact => await ExecuteShowArtifactAsync(args, ct),
                AgentToolNames.ScheduleJob => await ExecuteScheduleJobAsync(args, ct),
                AgentToolNames.ListSchedules => await ExecuteListSchedulesAsync(args, ct),
                AgentToolNames.ToggleSchedule => await ExecuteToggleScheduleAsync(args, ct),
                AgentToolNames.RunJob => await ExecuteRunJobAsync(args, ct),
                AgentToolNames.RunJobChain => await ExecuteRunJobChainAsync(args, ct),
                AgentToolNames.CallMcp => await ExecuteCallMcpAsync(args, ct),
                AgentToolNames.ListMcpTools => await ExecuteListMcpToolsAsync(args, ct),
                AgentToolNames.RenderMap => await ExecuteRenderMapAsync(args, ct),
                _ => ToolCallResult.Fail(ChatCopy.UnknownTool(toolName)),
            };
        }
        catch (Exception ex) { return ToolCallResult.Fail(ex.Message); }
    }

    private async Task<ToolCallResult> ExecuteQueryTableAsync(string args, CancellationToken ct)
    {
        var parts = args.Split('|');
        var tableName = parts.Length > 0 ? parts[0].Trim() : "";
        var page = parts.Length > 1 ? int.Parse(parts[1]) : 1;
        AddActiveAction(AgentToolNames.QueryTable, ChatCopy.QueryingTable(tableName));
        var result = await _svc.QueryProjectTablePageAsync(ProjectId!.Value, tableName, null, page, 50, ct: ct);
        var preview = string.Join("\n", result.Rows.Take(3).Select(r => string.Join(", ", r.Take(4))));
        AddFetchedData(tableName, (int)result.TotalCount, preview);
        var sb = new System.Text.StringBuilder();
        sb.Append($"Table: {tableName} ({result.TotalCount} rows)\n");
        sb.Append(string.Join(" | ", result.Columns));
        sb.Append("\n---\n");
        foreach (var row in result.Rows) { sb.Append(string.Join(" | ", row.Select(v => v?.ToString() ?? "null"))); sb.Append("\n"); }
        return ToolCallResult.Ok(sb.ToString());
    }

    private async Task<ToolCallResult> ExecuteListTablesAsync(CancellationToken ct)
    {
        AddActiveAction(AgentToolNames.ListTables, ChatCopy.LoadingTables);
        var tables = await _svc.ListProjectDataTablesAsync(ProjectId!.Value, ct);
        var sb = new System.Text.StringBuilder();
        sb.Append($"Project tables: {tables.Count}\n");
        foreach (var t in tables) sb.Append($"- {t.Name} ({t.RowEstimate} rows)\n");
        return ToolCallResult.Ok(sb.ToString());
    }

    private async Task<ToolCallResult> ExecuteListJobsAsync(CancellationToken ct)
    {
        AddActiveAction(AgentToolNames.ListJobs, ChatCopy.LoadingJobs);
        var jobs = await _svc.ListJobsAsync(ProjectId!.Value, ct);
        var sb = new System.Text.StringBuilder();
        sb.Append($"Jobs: {jobs.Count}\n");
        foreach (var j in jobs) sb.Append($"- {j.Name} (id: {j.Id})\n");
        return ToolCallResult.Ok(sb.ToString());
    }

    private async Task<ToolCallResult> ExecuteListJobRunsAsync(string args, CancellationToken ct)
    {
        AddActiveAction(AgentToolNames.ListJobRuns, args != "" ? $"Runs for {args[..8]}..." : "Loading all runs...");
        var jobId = Guid.TryParse(args, out var id) ? id : Guid.Empty;
        var runs = jobId != Guid.Empty ? await _svc.ListJobRunsAsync(jobId, ct) : new List<JobRunView>();
        var sb = new System.Text.StringBuilder();
        sb.Append($"Job runs: {runs.Count}\n");
        foreach (var r in runs.Take(20)) sb.Append($"- {r.Status} ({r.StartedAt:yyyy-MM-dd HH:mm})\n");
        return ToolCallResult.Ok(sb.ToString());
    }

    private async Task<ToolCallResult> ExecuteSearchAsync(string args, CancellationToken ct)
    {
        if (!ProjectId.HasValue) return ToolCallResult.Fail(ChatCopy.NoProjectSelected);
        var query = args.Trim();
        if (query.Length == 0) return ToolCallResult.Fail($"Usage: {AgentToolNames.FormatCall(AgentToolNames.Search, "query")}");

        AddActiveAction(AgentToolNames.Search, $"Searching \"{(query.Length > 30 ? query[..30] + "…" : query)}\"...");
        try
        {
            var matches = await _svc.SearchRunOutputsAsync(ProjectId.Value, query, 8, ct);
            if (matches.Count == 0) { CompleteActiveAction(AgentToolNames.Search, true); return ToolCallResult.Ok("No matching run outputs found. Semantic search may be disabled (no embedding API key configured)."); }
            var sb = new System.Text.StringBuilder();
            sb.Append($"Matches for \"{query}\": {matches.Count}\n");
            foreach (var m in matches) { var snippet = m.Text.Length > 300 ? m.Text[..300] + "…" : m.Text; sb.Append($"- (score {m.Score:0.00}, run {m.JobRunId.ToString()[..8]}) {snippet}\n"); }
            CompleteActiveAction(AgentToolNames.Search, true);
            AddToolHistory(AgentToolNames.Search, true, $"{matches.Count} matches");
            return ToolCallResult.Ok(sb.ToString());
        }
        catch (Exception ex) { CompleteActiveAction(AgentToolNames.Search, false); return ToolCallResult.Fail(ex.Message); }
    }

    private async Task<ToolCallResult> ExecuteGetArtifactsAsync(string args, CancellationToken ct)
    {
        if (!ProjectId.HasValue) return ToolCallResult.Fail(ChatCopy.NoProjectSelected);
        var query = args.Trim();
        AddActiveAction(AgentToolNames.GetArtifacts, string.IsNullOrEmpty(query) ? ChatCopy.LoadingArtifacts() : ChatCopy.SearchingArtifacts(query));
        try
        {
            IReadOnlyList<ArtifactFileView> artifacts;
            if (string.IsNullOrEmpty(query))
            {
                artifacts = await _svc.ListProjectArtifactsAsync(ProjectId.Value, 100, null, ct);
            }
            else
            {
                var terms = ArtifactSearchTerms(query);
                if (terms.Count == 0) artifacts = await _svc.ListProjectArtifactsAsync(ProjectId.Value, 100, null, ct);
                else { var broad = await _svc.ListProjectArtifactsAsync(ProjectId.Value, 2000, null, ct); artifacts = ScoreAndFilterArtifacts(broad, terms); }
            }
            if (artifacts.Count == 0) { CompleteActiveAction(AgentToolNames.GetArtifacts, true); return ToolCallResult.Ok(string.IsNullOrEmpty(query) ? ChatCopy.NoArtifactsYet : ChatCopy.ArtifactsMatchedNone(query)); }
            MergePanelArtifacts(artifacts);
            if (!string.IsNullOrEmpty(query) && artifacts.Count == 1) { CompleteActiveAction(AgentToolNames.GetArtifacts, true); return await ExecuteShowArtifactAsync(artifacts[0].Id.ToString(), ct); }
            var sb = new System.Text.StringBuilder();
            sb.Append($"Artifacts: {artifacts.Count}\n");
            foreach (var a in artifacts) sb.Append($"- {a.Title} | {a.Kind} | {Helpers.FormatHelper.Bytes(a.SizeBytes)} | {a.CreatedAt:yyyy-MM-dd HH:mm} | id:{a.Id} | /runs/{a.RunId}/artifacts/{a.Id}\n");
            sb.Append($"\nTo display one, call {AgentToolNames.FormatCall(AgentToolNames.ShowArtifact, "id")}.");
            CompleteActiveAction(AgentToolNames.GetArtifacts, true);
            AddToolHistory(AgentToolNames.GetArtifacts, true, $"{artifacts.Count} artifacts");
            return ToolCallResult.Ok(sb.ToString());
        }
        catch (Exception ex) { CompleteActiveAction(AgentToolNames.GetArtifacts, false); return ToolCallResult.Fail(ex.Message); }
    }

    private static IReadOnlyList<string> ArtifactSearchTerms(string query)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "the", "a", "an", "and", "or", "of", "in", "on", "at", "to", "for", "with", "from", "is", "are", "was", "were", "me", "show", "give", "list", "report", "reports", "file", "files", "artifact", "artifacts", "find", AgentToolNames.Search, "get" };
        return query.Split(new[] { ' ', ',', '.', '-', '_', '/', '\\', '|', '&' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 2 && !stopWords.Contains(t)).Distinct().ToList();
    }

    private static IReadOnlyList<ArtifactFileView> ScoreAndFilterArtifacts(IReadOnlyList<ArtifactFileView> artifacts, IReadOnlyList<string> terms)
        => artifacts.Select(a => { var haystack = $"{a.Title} {a.Kind}".ToLowerInvariant(); return (Artifact: a, Score: terms.Count(t => haystack.Contains(t))); }).Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenByDescending(x => x.Artifact.CreatedAt).Take(25).Select(x => x.Artifact).ToList();

    internal async Task<ToolCallResult> ExecuteShowArtifactAsync(string args, CancellationToken ct)
    {
        if (!ProjectId.HasValue) return ToolCallResult.Fail(ChatCopy.NoProjectSelected);
        var idStr = args.Trim().Split('|')[0].Trim();
        if (!Guid.TryParse(idStr, out var artifactId)) return ToolCallResult.Fail($"Usage: {AgentToolNames.FormatCall(AgentToolNames.ShowArtifact, "artifactId")}");

        AddActiveAction(AgentToolNames.ShowArtifact, ChatCopy.LoadingArtifact(artifactId.ToString()[..8]));
        try
        {
            var link = await _links.GetByIdAsync(artifactId, ct);
            if (link is null) { CompleteActiveAction(AgentToolNames.ShowArtifact, false); return ToolCallResult.Fail(ChatCopy.ArtifactNotFound); }
            var isTextLike = IsTextArtifactContentType(link.ContentType);
            var isDocument = IsDocumentContentType(link.ContentType);
            string? content = null, extractedText = null;
            var truncated = false; var extractedTruncated = false;
            if ((isTextLike || isDocument) && _objectStore.IsEnabled)
            {
                try
                {
                    var obj = await _objectStore.OpenReadAsync(link.Bucket, link.ObjectKey, ct);
                    if (obj is not null)
                    {
                        await using var stream = obj.Content;
                        using var ms = new MemoryStream();
                        var buffer = new byte[81920]; int read, total = 0;
                        var maxBytes = isDocument ? MaxArtifactDocumentBytes : MaxArtifactInlineBytes;
                        while ((read = await stream.ReadAsync(buffer, ct)) > 0) { total += read; if (total > maxBytes) { truncated = true; break; } ms.Write(buffer, 0, read); }
                        var bytes = ms.ToArray();
                        if (isDocument) { var rawText = _docExtractor.ExtractText(bytes, link.Title); if (!string.IsNullOrWhiteSpace(rawText)) { if (rawText.Length > MaxArtifactExtractedTextLength) { extractedText = rawText[..MaxArtifactExtractedTextLength]; extractedTruncated = true; } else extractedText = rawText; } }
                        else content = System.Text.Encoding.UTF8.GetString(bytes);
                    }
                }
                catch { }
            }
            if (_contentIndexer is { IsEnabled: true })
            {
                var indexText = extractedText ?? content;
                if (!string.IsNullOrWhiteSpace(indexText))
                { try { await _contentIndexer.IndexAsync(ProjectId!.Value, ContentKind.Document, $"artifact:{link.Id}", $"{link.Title}\n\n{indexText}", ct); } catch { } }
            }
            var payload = new { link.Id, link.RunId, link.Title, link.ContentType, link.SizeBytes, IsText = isTextLike, Content = content, ExtractedText = extractedText, ExtractedTruncated = extractedTruncated, Truncated = truncated };
            CompleteActiveAction(AgentToolNames.ShowArtifact, true);
            AddToolHistory(AgentToolNames.ShowArtifact, true, link.Title);
            return ToolCallResult.Artifact(System.Text.Json.JsonSerializer.Serialize(payload));
        }
        catch (Exception ex) { CompleteActiveAction(AgentToolNames.ShowArtifact, false); return ToolCallResult.Fail(ex.Message); }
    }

    private static bool IsTextArtifactContentType(string ct) => ct.StartsWith("text/") || ct.Contains("json", StringComparison.OrdinalIgnoreCase) || ct.Contains("csv", StringComparison.OrdinalIgnoreCase) || ct.Contains("html", StringComparison.OrdinalIgnoreCase) || ct.Contains("xml", StringComparison.OrdinalIgnoreCase) || ct.Contains("svg", StringComparison.OrdinalIgnoreCase);
    private static bool IsDocumentContentType(string ct) => ct == "application/pdf" || ct == "application/msword" || ct == "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private async Task<ToolCallResult> ExecuteRenderGraphAsync(string args, CancellationToken ct)
    {
        if (!ProjectId.HasValue) return ToolCallResult.Fail(ChatCopy.NoProjectSelected);
        var parts = args.Split('|');
        var chartType = parts.Length > 0 ? parts[0].Trim() : "bar";
        var tableName = parts.Length > 1 ? parts[1].Trim() : "";
        var column = parts.Length > 2 ? parts[2].Trim() : "";
        var tableValid = false;
        if (!string.IsNullOrEmpty(tableName)) { try { var probe = await _svc.QueryProjectTablePageAsync(ProjectId!.Value, tableName, null, 1, 1, ct: ct); tableValid = probe.Columns.Count > 0; } catch { tableValid = false; } }
        if (!tableValid)
        {
            var tables = await _svc.ListProjectDataTablesAsync(ProjectId!.Value, ct);
            if (tables.Count == 0) return ToolCallResult.Fail("No data tables found in this project.");
            var clarify = await AskClarificationAsync(new ClarificationRequest { ToolName = AgentToolNames.RenderGraph, Args = args, Question = $"Table '{tableName}' not found. Which table would you like to chart?", MultiSelect = false, Options = tables.Where(t => t.RowEstimate > 0).Select(t => new ClarificationOption { Id = t.Name, Label = t.Name, Description = $"~{t.RowEstimate} rows" }).ToList() });
            if (!clarify.Confirmed || clarify.SelectedIds.Count == 0) return ToolCallResult.Fail("Cancelled — no table selected.");
            tableName = clarify.SelectedIds[0];
        }
        var columns = new List<string>();
        if (!string.IsNullOrEmpty(column))
        { var probe = await _svc.QueryProjectTablePageAsync(ProjectId!.Value, tableName, null, 1, 1, ct: ct); if (probe.Columns.Any(c => c.Equals(column, StringComparison.OrdinalIgnoreCase))) columns.Add(column); }
        if (columns.Count == 0)
        {
            var probe = await _svc.QueryProjectTablePageAsync(ProjectId!.Value, tableName, null, 1, 1, ct: ct);
            if (probe.Columns.Count == 0) return ToolCallResult.Fail($"Table '{tableName}' has no columns.");
            var numericProbe = await _svc.QueryProjectTablePageAsync(ProjectId!.Value, tableName, null, 1, 10, ct: ct);
            var numericCols = new List<string>();
            for (var i = 0; i < numericProbe.Columns.Count; i++) { if (numericProbe.Rows.Any(r => i < r.Count && double.TryParse(r[i]?.ToString(), out _))) numericCols.Add(numericProbe.Columns[i]); }
            var clarify = await AskClarificationAsync(new ClarificationRequest { ToolName = AgentToolNames.RenderGraph, Args = args, Question = $"Which column(s) in '{tableName}' should be charted?", MultiSelect = true, Options = numericCols.Select(c => new ClarificationOption { Id = c, Label = c }).ToList() });
            if (!clarify.Confirmed || clarify.SelectedIds.Count == 0) return ToolCallResult.Fail("Cancelled — no column selected.");
            columns = clarify.SelectedIds;
        }
        var result = await _svc.QueryProjectTablePageAsync(ProjectId!.Value, tableName, null, 1, 100, ct: ct);
        var labels = new List<string>(); var seriesList = new List<(string Name, List<double> Values)>();
        foreach (var col in columns) { var colIndex = result.Columns.ToList().FindIndex(c => c.Equals(col, StringComparison.OrdinalIgnoreCase)); if (colIndex >= 0) seriesList.Add((col, new List<double>())); }
        if (seriesList.Count == 0) return ToolCallResult.Fail($"None of the selected columns found in '{tableName}'.");
        foreach (var row in result.Rows) { var label = row[0]?.ToString() ?? ""; labels.Add(label); for (var s = 0; s < seriesList.Count; s++) { var colIndex = result.Columns.ToList().FindIndex(c => c.Equals(columns[s], StringComparison.OrdinalIgnoreCase)); var valStr = colIndex >= 0 && colIndex < row.Count ? row[colIndex]?.ToString() ?? "0" : "0"; seriesList[s].Values.Add(double.TryParse(valStr, out var val) ? val : 0); } }
        var graphData = new { type = chartType, title = $"{tableName}" + (columns.Count > 1 ? $" — {string.Join(", ", columns)}" : $" — {columns[0]}"), labels = labels.Take(24).ToList(), series = seriesList.Select(s => new { name = s.Name, values = s.Values.Take(24).ToList() }).ToList() };
        return ToolCallResult.Graph(System.Text.Json.JsonSerializer.Serialize(graphData));
    }

    private async Task<ToolCallResult> ExecuteQueryGraphAsync(CancellationToken ct)
    {
        if (!ProjectId.HasValue) return ToolCallResult.Fail(ChatCopy.NoProjectSelected);
        AddActiveAction(AgentToolNames.QueryGraph, "Loading project graph...");
        try
        {
            var graph = await _svc.GetGraphVizAsync(ProjectId!.Value, ct);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Project Dependency Graph\n- **Nodes:** {graph.NodeCount}\n- **Links:** {graph.LinkCount}\n");
            var byKind = graph.Nodes.GroupBy(n => n.Kind ?? "unknown").OrderByDescending(g => g.Count()).ToList();
            sb.AppendLine("## Entity types");
            foreach (var kind in byKind.Take(10)) sb.AppendLine($"- **{kind.Key}:** {kind.Count()} nodes");
            sb.AppendLine();
            var hubs = graph.Nodes.Where(n => n.Degree >= 5).OrderByDescending(n => n.Degree).Take(10).ToList();
            if (hubs.Count > 0) { sb.AppendLine("## Key entities (hubs)"); foreach (var n in hubs) sb.AppendLine($"- **{n.Label}** ({n.Degree} connections){(n.IsGod ? " ⭐" : "")}"); sb.AppendLine(); }
            GraphNodes.Clear(); GraphNodes.AddRange(graph.Nodes.OrderByDescending(n => n.Degree).Take(50)); GraphLinks = graph.LinkCount;
            var nodeLabels = graph.Nodes.ToDictionary(n => n.Id, n => n.Label);
            if (graph.Links.Count > 0) { sb.AppendLine("\n**All relationships:**"); foreach (var edge in graph.Links) sb.AppendLine($"- {nodeLabels.GetValueOrDefault(edge.Source) ?? "?"} → {nodeLabels.GetValueOrDefault(edge.Target) ?? "?"} ({edge.Confidence})"); }
            CompleteActiveAction(AgentToolNames.QueryGraph, true); AddToolHistory(AgentToolNames.QueryGraph, true, $"{graph.NodeCount} nodes");
            return ToolCallResult.Ok(sb.ToString());
        }
        catch (Exception ex) { CompleteActiveAction(AgentToolNames.QueryGraph, false); AddToolHistory(AgentToolNames.QueryGraph, false, ex.Message); return ToolCallResult.Fail($"Graph query failed: {ex.Message}"); }
    }

    private async Task<ToolCallResult> ExecuteRenderMapAsync(string args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args)) return ToolCallResult.Fail(ChatCopy.RenderMapUsage);
        try { System.Text.Json.JsonDocument.Parse(args.Trim()); } catch { return ToolCallResult.Fail(ChatCopy.InvalidRenderMapJson); }
        AddActiveAction(AgentToolNames.RenderMap, ChatCopy.RenderingMap);
        await Task.Delay(10, ct);
        CompleteActiveAction(AgentToolNames.RenderMap, true); AddToolHistory(AgentToolNames.RenderMap, true, ChatCopy.MapRendered);
        return ToolCallResult.Map(args.Trim());
    }

    private async Task<ToolCallResult> ExecuteScheduleJobAsync(string args, CancellationToken ct)
    {
        var parts = args.Split('|');
        if (parts.Length < 3) return ToolCallResult.Fail("Usage: schedule_job|jobId|name|cron");
        var jobId = Guid.TryParse(parts[0].Trim(), out var id) ? id : Guid.Empty;
        if (jobId == Guid.Empty) return ToolCallResult.Fail("Invalid jobId");
        AddActiveAction(AgentToolNames.ScheduleJob, $"Creating schedule for job {jobId.ToString()[..8]}...");
        var trigger = await _svc.CreateTriggerAsync(new CreateTriggerCommand(jobId, parts[1].Trim(), "Schedule", parts[2].Trim(), null), ct);
        CompleteActiveAction(AgentToolNames.ScheduleJob, true); AddToolHistory(AgentToolNames.ScheduleJob, true, $"Next: {trigger.NextRunAt?.ToString("HH:mm") ?? "—"}");
        return ToolCallResult.Ok($"Schedule created: {trigger.Name} (id: {trigger.Id})\nCron: {trigger.CronExpression}\nNext run: {trigger.NextRunAt?.ToString("yyyy-MM-dd HH:mm") ?? "—"}\nEnabled: {trigger.Enabled}");
    }

    private async Task<ToolCallResult> ExecuteListSchedulesAsync(string args, CancellationToken ct)
    {
        if (!ProjectId.HasValue) return ToolCallResult.Fail(ChatCopy.NoProjectSelected);
        var jobId = Guid.TryParse(args.Trim(), out var id) ? id : Guid.Empty;
        var triggers = await _svc.ListTriggersAsync(ProjectId!.Value, ct);
        if (jobId != Guid.Empty) triggers = triggers.Where(t => t.JobId == jobId).ToList();
        var sb = new System.Text.StringBuilder(); sb.Append($"Schedules: {triggers.Count}\n");
        foreach (var t in triggers) { sb.Append($"- {t.Name} ({t.Kind})\n"); if (t.Kind == "Schedule") sb.Append($"  Cron: {t.CronExpression} | Next: {t.NextRunAt?.ToString("yyyy-MM-dd HH:mm") ?? "—"}\n"); sb.Append($"  Enabled: {t.Enabled} | Last fired: {t.LastFiredAt?.ToString("yyyy-MM-dd HH:mm") ?? "never"}\n"); }
        return ToolCallResult.Ok(sb.ToString());
    }

    private async Task<ToolCallResult> ExecuteToggleScheduleAsync(string args, CancellationToken ct)
    {
        var parts = args.Split('|');
        if (parts.Length < 2) return ToolCallResult.Fail("Usage: toggle_schedule|triggerId|true|false");
        var triggerId = Guid.TryParse(parts[0].Trim(), out var id) ? id : Guid.Empty;
        if (triggerId == Guid.Empty) return ToolCallResult.Fail("Invalid triggerId");
        var enabled = parts[1].Trim().ToLower() == "true";
        AddActiveAction(AgentToolNames.ToggleSchedule, $"Toggling schedule {triggerId.ToString()[..8]}...");
        var trigger = await _svc.SetTriggerEnabledAsync(triggerId, enabled, ct);
        CompleteActiveAction(AgentToolNames.ToggleSchedule, true); AddToolHistory(AgentToolNames.ToggleSchedule, true, enabled ? "enabled" : "disabled");
        return ToolCallResult.Ok($"Schedule '{trigger.Name}' {(enabled ? "enabled" : "disabled")}.\nNext run: {trigger.NextRunAt?.ToString("yyyy-MM-dd HH:mm") ?? "—"}");
    }

    private async Task<ToolCallResult> ExecuteListChainsAsync(CancellationToken ct)
    {
        AddActiveAction(AgentToolNames.ListChains, ChatCopy.LoadingChains);
        var chains = await _svc.ListJobChainsAsync(ProjectId!.Value, ct);
        var sb = new System.Text.StringBuilder();
        sb.Append($"Job chains: {chains.Count}\n");
        foreach (var c in chains) sb.Append($"- {c.Name} (id: {c.Id}, {c.Steps.Count} steps)\n");
        CompleteActiveAction(AgentToolNames.ListChains, true);
        AddToolHistory(AgentToolNames.ListChains, true, $"{chains.Count} chains");
        return ToolCallResult.Ok(sb.ToString());
    }

    private async Task<ToolCallResult> ExecuteRunJobAsync(string args, CancellationToken ct)
    {
        var jobId = Guid.TryParse(args.Trim(), out var id) ? id : Guid.Empty;
        if (jobId == Guid.Empty) return ToolCallResult.Fail("Invalid jobId");
        AddActiveAction(AgentToolNames.RunJob, ChatCopy.RunningJob(jobId.ToString()[..8]));
        var run = await _svc.RunJobAsync(jobId, null, null, ct);
        CompleteActiveAction(AgentToolNames.RunJob, true); AddToolHistory(AgentToolNames.RunJob, true, run.Status);
        return ToolCallResult.Ok($"Job run started: {run.Id}\nStatus: {run.Status}\nStarted: {run.StartedAt:yyyy-MM-dd HH:mm}");
    }

    private async Task<ToolCallResult> ExecuteRunJobChainAsync(string args, CancellationToken ct)
    {
        // args: "chainIdOrName" or "chainIdOrName|payloadJson" — split only on the FIRST pipe so the
        // payload may itself contain pipes.
        var pipeIdx = args.IndexOf('|');
        var key = (pipeIdx >= 0 ? args[..pipeIdx] : args).Trim();
        var payload = pipeIdx >= 0 ? args[(pipeIdx + 1)..].Trim() : null;
        if (string.IsNullOrEmpty(payload)) payload = null;

        var resolved = await ResolveChainIdAsync(key, ct);
        if (resolved.Error is not null)
            return ToolCallResult.Fail(resolved.Error);

        var chainId = resolved.ChainId;
        AddActiveAction(AgentToolNames.RunJobChain, ChatCopy.RunningChain(chainId.ToString()[..8]));
        var run = await _svc.RunJobChainAsync(chainId, payload, chainRunId: null, stepPayloadOverrides: null, ct: ct);
        var sb = new System.Text.StringBuilder();
        sb.Append($"Chain run: {run.Id}\nStatus: {run.Status}\n");
        foreach (var step in run.Steps)
            sb.Append($"- {step.JobName}: {step.Status}\n");
        if (!string.IsNullOrEmpty(run.FinalOutput))
        {
            var output = run.FinalOutput.Length > 2000 ? run.FinalOutput[..2000] + "…" : run.FinalOutput;
            sb.Append($"\nFinal output:\n{output}");
        }
        CompleteActiveAction(AgentToolNames.RunJobChain, true);
        AddToolHistory(AgentToolNames.RunJobChain, true, run.Status);
        return ToolCallResult.Ok(sb.ToString());
    }

    private async Task<(Guid ChainId, string? Error)> ResolveChainIdAsync(string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            return (Guid.Empty, "Usage: run_job_chain|chainIdOrName|payloadJson");
        if (Guid.TryParse(key, out var chainId) && chainId != Guid.Empty)
            return (chainId, null);
        if (!ProjectId.HasValue)
            return (Guid.Empty, ChatCopy.NoProjectSelected);

        var chains = await _svc.ListJobChainsAsync(ProjectId.Value, ct);
        var matches = chains
            .Where(c => string.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 1)
            return (matches[0].Id, null);
        if (matches.Count > 1)
            return (Guid.Empty, $"Multiple chains named '{key}' — use the chain id instead");

        var available = chains.Count == 0
            ? "(none)"
            : string.Join(", ", chains.Select(c => $"{c.Name} ({c.Id})"));
        return (Guid.Empty, $"Unknown chain '{key}'. Available: {available}");
    }

    private async Task<ToolCallResult> ExecuteCallMcpAsync(string args, CancellationToken ct)
    {
        var parts = args.Split('|');
        if (parts.Length < 2) return ToolCallResult.Fail("Usage: call_mcp|serverName|toolName|[jsonArgs]");
        var serverName = parts[0].Trim(); var toolName = parts[1].Trim(); var jsonArgs = parts.Length > 2 ? parts[2].Trim() : "{}";
        AddActiveAction(AgentToolNames.CallMcp, ChatCopy.CallingMcp(serverName, toolName));
        var connections = await _svc.ListMcpConnectionsAsync(ProjectId!.Value, ct);
        var connection = connections.FirstOrDefault(c => c.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));
        if (connection == null) { CompleteActiveAction(AgentToolNames.CallMcp, false); return ToolCallResult.Fail(ChatCopy.McpServerNotFound(serverName, string.Join(", ", connections.Select(c => c.Name)))); }
        try
        {
            var arguments = System.Text.Json.JsonDocument.Parse(jsonArgs).RootElement;
            var result = await _mcpClient.CallToolAsync(connection.Id, toolName, arguments, ct);
            CompleteActiveAction(AgentToolNames.CallMcp, result.Success); AddToolHistory(AgentToolNames.CallMcp, result.Success, result.Success ? "ok" : result.Error ?? "error");
            return result.Success ? ToolCallResult.Ok(result.Content ?? "Tool executed successfully") : ToolCallResult.Fail($"MCP tool error: {result.Error}");
        }
        catch (Exception ex) { CompleteActiveAction(AgentToolNames.CallMcp, false); return ToolCallResult.Fail(ChatCopy.McpCallFailed(ex.Message)); }
    }

    private async Task<ToolCallResult> ExecuteListMcpToolsAsync(string args, CancellationToken ct)
    {
        var serverName = args.Trim();
        var connections = await _svc.ListMcpConnectionsAsync(ProjectId!.Value, ct);
        if (connections.Count == 0) return ToolCallResult.Ok("No MCP servers configured. Add one in Settings → MCP Servers.");
        var sb = new System.Text.StringBuilder();
        if (string.IsNullOrEmpty(serverName))
        {
            sb.AppendLine("Available MCP servers:");
            foreach (var conn in connections) sb.AppendLine($"- {conn.Name} ({conn.Transport}) - {(conn.Enabled ? "enabled" : "disabled")}");
            sb.AppendLine("\nUse list_mcp_tools|serverName to see available tools.");
        }
        else
        {
            var connection = connections.FirstOrDefault(c => c.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));
            if (connection == null) return ToolCallResult.Fail($"MCP server '{serverName}' not found.");
            var tools = await _mcpClient.ListToolsAsync(connection.Id, ct);
            sb.AppendLine($"Tools on {serverName}:");
            foreach (var tool in tools) sb.AppendLine($"- {tool.Name}: {tool.Description ?? "No description"}");
        }
        return ToolCallResult.Ok(sb.ToString());
    }

    public static List<string> ParseNumberedOptions(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return new();
        var matches = System.Text.RegularExpressions.Regex.Matches(content, @"^\s*\d+[.)]\s+(.+)$", System.Text.RegularExpressions.RegexOptions.Multiline);
        if (matches.Count < 2) return new();
        return matches.Select(m => m.Groups[1].Value.Trim()).ToList();
    }
}
