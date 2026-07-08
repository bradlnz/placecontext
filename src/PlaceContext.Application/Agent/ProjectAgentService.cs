using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>What one agent pass concluded: its assessment plus the work items it queued.</summary>
public sealed record AgentPassResult(string Assessment, IReadOnlyList<string> QueuedTitles);

/// <summary>
/// The per-project agent: reviews where a project is — recent activity, the open work queue, and
/// how its job runs have been going — and figures out what to do next, queuing the suggestions as
/// work items (deduplicated against what is already open, so repeated passes stay quiet). The
/// reasoning comes from the local LLM (Ollama in-cluster) when one is configured; without it a
/// deterministic pass still surfaces the obvious: failed runs to investigate and a stalled queue.
/// Driven on a schedule by the host's ProjectAgentSchedulerService.
/// </summary>
public sealed class ProjectAgentService
{
    private const int MaxSuggestionsPerPass = 3;

    private readonly IProjectRepository _projects;
    private readonly IActivityLogRepository _activity;
    private readonly IWorkItemRepository _workItems;
    private readonly IJobRunRepository _runs;
    private readonly ILlmGateway _llm;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<ProjectAgentService>? _log;

    public ProjectAgentService(
        IProjectRepository projects,
        IActivityLogRepository activity,
        IWorkItemRepository workItems,
        IJobRunRepository runs,
        ILlmGateway llm,
        IUnitOfWork uow,
        IClock clock,
        ILogger<ProjectAgentService>? log = null)
    {
        _projects = projects;
        _activity = activity;
        _workItems = workItems;
        _runs = runs;
        _llm = llm;
        _uow = uow;
        _clock = clock;
        _log = log;
    }

    /// <summary>Run one pass for a project. Returns null when the project doesn't exist.</summary>
    public async Task<AgentPassResult?> RunAsync(Guid projectId, CancellationToken ct = default)
    {
        var pid = ProjectId.From(projectId);
        var project = await _projects.GetByIdAsync(pid, ct);
        if (project is null) return null;

        var ledger = await _activity.GetForProjectAsync(pid, ct);
        var recentActivity = ledger.Records.OrderByDescending(r => r.Sequence).Take(10).ToList();
        var openItems = (await _workItems.ListForProjectAsync(pid, ct))
            .Where(w => w.Status != WorkItemStatus.Done)
            .ToList();
        var recentRuns = (await _runs.ListRecentAsync(50, ct))
            .Where(r => r.ProjectId == projectId)
            .Take(10)
            .ToList();

        var (assessment, proposals) = await ProposeAsync(project, recentActivity, openItems, recentRuns, ct);

        // Dedupe against everything already open — the agent must never nag the same step twice.
        var openTitles = new HashSet<string>(openItems.Select(w => w.Title.Trim()), StringComparer.OrdinalIgnoreCase);
        var queued = new List<string>();
        foreach (var p in proposals.Take(MaxSuggestionsPerPass))
        {
            if (string.IsNullOrWhiteSpace(p.Title) || !openTitles.Add(p.Title.Trim()))
                continue;
            var detail = $"Proposed by the project agent — {assessment}" +
                         (string.IsNullOrWhiteSpace(p.Detail) ? "" : $"\n\n{p.Detail}");
            await _workItems.AddAsync(WorkItem.Queue(pid, p.Title.Trim(), detail, p.Priority, _clock.UtcNow), ct);
            queued.Add(p.Title.Trim());
        }
        if (queued.Count > 0)
            await _uow.SaveChangesAsync(ct);

        _log?.LogInformation("Project agent pass for {Project}: {Queued} suggestion(s) queued.", project.Name, queued.Count);
        return new AgentPassResult(assessment, queued);
    }

    private sealed record Proposal(string Title, string? Detail, WorkItemPriority Priority);

    private async Task<(string Assessment, IReadOnlyList<Proposal> Proposals)> ProposeAsync(
        Project project,
        IReadOnlyList<ActivityRecord> recentActivity,
        IReadOnlyList<WorkItem> openItems,
        IReadOnlyList<JobRun> recentRuns,
        CancellationToken ct)
    {
        if (_llm.IsEnabled)
        {
            try
            {
                var reply = await _llm.GenerateAsync(SystemPrompt, BuildContext(project, recentActivity, openItems, recentRuns), ct);
                if (TryParseReply(reply, out var parsed))
                    return parsed;
                _log?.LogWarning("Project agent: LLM reply was not parseable JSON; using the deterministic pass.");
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Project agent: LLM call failed; using the deterministic pass.");
            }
        }
        return Deterministic(recentActivity, openItems, recentRuns);
    }

    private const string SystemPrompt =
        "You are the project's resident agent inside PlaceContext. From the project state you are " +
        "given, decide what should happen next. Reply with ONLY this JSON, nothing else: " +
        "{\"assessment\":\"one or two sentences on where the project is\"," +
        "\"nextSteps\":[{\"title\":\"imperative, <=80 chars\",\"detail\":\"why + how\"," +
        "\"priority\":\"Low|Normal|High\"}]} — at most 3 nextSteps, most important first. " +
        "Do not repeat anything already in the open work queue.";

    private static string BuildContext(
        Project project, IReadOnlyList<ActivityRecord> activity, IReadOnlyList<WorkItem> open, IReadOnlyList<JobRun> runs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Project: {project.Name}");
        sb.AppendLine("\n## Recent activity (newest first)");
        if (activity.Count == 0) sb.AppendLine("(none recorded)");
        foreach (var a in activity)
            sb.AppendLine($"- {a.RecordedAt:yyyy-MM-dd} {a.Author.Name}: {a.Summary}");
        sb.AppendLine("\n## Open work queue");
        if (open.Count == 0) sb.AppendLine("(empty)");
        foreach (var w in open)
            sb.AppendLine($"- [{w.Priority}] {w.Title}");
        sb.AppendLine("\n## Recent job runs");
        if (runs.Count == 0) sb.AppendLine("(none)");
        foreach (var r in runs)
            sb.AppendLine($"- {r.StartedAt:yyyy-MM-dd HH:mm} status={r.Status}");
        return sb.ToString();
    }

    private static bool TryParseReply(string reply, out (string, IReadOnlyList<Proposal>) parsed)
    {
        parsed = default!;
        // Models often wrap JSON in prose/fences — take the outermost object.
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start) return false;
        try
        {
            using var doc = JsonDocument.Parse(reply[start..(end + 1)]);
            var root = doc.RootElement;
            var assessment = root.TryGetProperty("assessment", out var a) && a.ValueKind == JsonValueKind.String
                ? a.GetString()!.Trim()
                : "reviewed the project state";
            var proposals = new List<Proposal>();
            if (root.TryGetProperty("nextSteps", out var steps) && steps.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in steps.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object
                        || !s.TryGetProperty("title", out var t) || t.ValueKind != JsonValueKind.String)
                        continue;
                    var detail = s.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                    var prio = s.TryGetProperty("priority", out var p) && p.ValueKind == JsonValueKind.String
                               && Enum.TryParse<WorkItemPriority>(p.GetString(), ignoreCase: true, out var wp)
                        ? wp
                        : WorkItemPriority.Normal;
                    proposals.Add(new Proposal(t.GetString()!, detail, prio));
                }
            }
            parsed = (assessment, proposals);
            return proposals.Count > 0 || assessment.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>No LLM (or it failed): surface the objectively actionable signals.</summary>
    private static (string, IReadOnlyList<Proposal>) Deterministic(
        IReadOnlyList<ActivityRecord> activity, IReadOnlyList<WorkItem> open, IReadOnlyList<JobRun> runs)
    {
        var proposals = new List<Proposal>();
        var failed = runs.Count(r => r.Status == JobRunStatus.Failed);
        if (failed > 0)
            proposals.Add(new Proposal(
                $"Investigate {failed} failed job run(s)",
                "Recent runs ended in Failed — open the run detail to see shard logs and artifacts.",
                WorkItemPriority.High));
        if (activity.Count == 0)
            proposals.Add(new Proposal(
                "Record the project's recent work in the activity log",
                "No activity has been recorded — the graph, risk, and reports all build on it.",
                WorkItemPriority.Normal));
        if (open.Count > 5)
            proposals.Add(new Proposal(
                "Triage the work queue",
                $"{open.Count} items are open — close or reprioritise so the next step is obvious.",
                WorkItemPriority.Low));
        var assessment = $"deterministic pass: {runs.Count} recent run(s) ({failed} failed), " +
                         $"{open.Count} open work item(s), {activity.Count} recent activity record(s)";
        return (assessment, proposals);
    }
}
