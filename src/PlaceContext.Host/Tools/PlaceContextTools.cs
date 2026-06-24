using System.ComponentModel;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using PlaceContext.Application;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace PlaceContext.Host.Tools;

/// <summary>
/// The MCP tool surface a coding agent calls. Each tool is a thin delegate to
/// <see cref="IPlaceContextService"/> — no business logic here. Every call is timed and recorded into
/// <see cref="IToolCallLog"/> so it shows up live on the portal's MCP Inspector. Results are pretty
/// JSON so they read well in an agent's context.
/// </summary>
[McpServerToolType]
public sealed class PlaceContextTools
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    // Deliberately ungated: a Viewer (or a stale/ghost token) must be able to call this to discover *why*
    // its writes are rejected. Reports the token's embedded identity and cross-checks it against the DB.
    [McpServerTool(Name = "whoami"), Description("Diagnose the calling access token: returns its user id, tenant, and embedded role, and cross-checks whether that user still exists in this tenant and what role the database holds for them. Use this when writes are rejected to confirm the token isn't a stale/ghost session (role stuck at Viewer, or a user id from a previous seed).")]
    public static Task<string> WhoAmI(IHttpContextAccessor http, IToolCallLog log, IMembershipService members, ICurrentTenant tenant)
        => Traced(log, "whoami", "—", "whoami", new { }, async () =>
        {
            var user = http.HttpContext?.User;
            var tokenRole = user?.FindFirst(ClaimTypes.Role)?.Value ?? user?.FindFirst("role")?.Value;
            var subRaw = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user?.FindFirst("sub")?.Value;
            Guid.TryParse(subRaw, out var userId);

            var dbMember = (await members.ListMembersAsync()).FirstOrDefault(m => m.Id == userId);

            return (object)new
            {
                tokenUserId = subRaw,
                tokenRole,
                tenantId = tenant.TenantId,
                tenantSlug = tenant.Slug,
                existsInTenant = dbMember is not null,
                dbRole = dbMember?.Role,
                email = dbMember?.Email,
                displayName = dbMember?.DisplayName,
                note = dbMember is null
                    ? "Token user not found in this tenant — a stale/ghost session (e.g. from a previous DB seed). Sign out of the portal, sign in as a current user, then re-authorize the MCP client to mint a fresh token."
                    : !string.Equals(dbMember.Role, tokenRole, StringComparison.OrdinalIgnoreCase)
                        ? $"Token role '{tokenRole}' is stale — the database now has '{dbMember.Role}'. Sign out and back in to refresh the portal cookie, then re-authorize MCP."
                        : "Token matches the database.",
            };
        });

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "create_project"), Description("Register a project with PlaceContext by its absolute repo path. Idempotent: re-creating a known path returns the existing project. New projects are created already registered.")]
    public static Task<string> CreateProject(IPlaceContextService svc, IToolCallLog log,
        [Description("Absolute path of the project repo, e.g. /home/brad/code/myapp")] string path,
        [Description("Optional display name; defaults to the last path segment")] string? name = null)
        => Traced(log, "create_project", "—", $"create {path}", new { path, name },
            () => svc.CreateProjectAsync(path, name));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "onboard"), Description("Bootstrap a repo into PlaceContext in one call: create the project (with initial debt), backfill the change ledger from git history, seed context from the repo's README/AGENTS/CLAUDE, and scaffold a local skill + agent for the target coding agent. Returns a setup summary.")]
    public static Task<string> Onboard(IPlaceContextService svc, IToolCallLog log,
        [Description("Absolute path of the project repo")] string path,
        [Description("Optional display name; defaults to the last path segment")] string? name = null,
        [Description("Target coding agent for the scaffolded skill/agent: 'claude' or 'codex'")] string agent = "claude",
        [Description("How many recent commits to backfill into the ledger")] int backfillLimit = 50)
        => Traced(log, "onboard", "—", $"onboard {path}", new { path, name, agent, backfillLimit },
            () => svc.OnboardAsync(path, name, agent, backfillLimit));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "add_work_item"), Description("Queue a work item (a change to be done) for a project. Priority is Low, Normal, or High.")]
    public static Task<string> AddWorkItem(IPlaceContextService svc, IToolCallLog log,
        Guid projectId, string title, string? detail = null, string priority = "Normal")
        => Traced(log, "add_work_item", projectId.ToString(), $"queue {title}", new { projectId, title, detail, priority },
            () => svc.AddWorkItemAsync(projectId, title, detail, priority));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "next_work_item"), Description("Claim the next queued work item for a project (highest priority, then oldest) and mark it in-progress. Returns null if the queue is empty. Use this to pick up what to work on next.")]
    public static Task<string> NextWorkItem(IPlaceContextService svc, IToolCallLog log, Guid projectId)
        => Traced(log, "next_work_item", projectId.ToString(), "claim next", new { projectId },
            () => svc.NextWorkItemAsync(projectId));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "complete_work_item"), Description("Mark a work item finished once the change is done (and recorded via record_change).")]
    public static Task<string> CompleteWorkItem(IPlaceContextService svc, IToolCallLog log, Guid workItemId)
        => Traced(log, "complete_work_item", "—", "complete work item", new { workItemId },
            () => svc.CompleteWorkItemAsync(workItemId));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "list_work_items"), Description("List a project's work-queue items (queued, in-progress, and done).")]
    public static Task<string> ListWorkItems(IPlaceContextService svc, IToolCallLog log, Guid projectId)
        => Traced(log, "list_work_items", projectId.ToString(), "list work items", new { projectId },
            () => svc.GetWorkItemsAsync(projectId));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "list_projects"), Description("List all projects PlaceContext is tracking, with their debt bands.")]
    public static Task<string> ListProjects(IPlaceContextService svc, IToolCallLog log)
        => Traced(log, "list_projects", "—", "list all projects", new { },
            () => svc.GetProjectsAsync());

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "register_project"), Description("Promote a discovered project to registered (watched) status.")]
    public static Task<string> RegisterProject(IPlaceContextService svc, IToolCallLog log,
        [Description("The project's GUID id")] Guid projectId)
        => Traced(log, "register_project", projectId.ToString(), "register project", new { projectId },
            () => svc.RegisterProjectAsync(projectId));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "rebuild_graph"), Description("Rebuild a project's decision tree from logged activity (decisions, changes, tool calls) and record the snapshot.")]
    public static Task<string> RebuildGraph(IPlaceContextService svc, IToolCallLog log, Guid projectId, bool incremental = true)
        => Traced(log, "rebuild_graph", projectId.ToString(), "rebuild decision tree", new { projectId, incremental },
            () => svc.RebuildGraphAsync(projectId, incremental));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "get_project_overview"), Description("Get a project's overview: status, graph stats, god nodes, debt, and change count.")]
    public static Task<string> GetProjectOverview(IPlaceContextService svc, IToolCallLog log, Guid projectId)
        => Traced(log, "get_project_overview", projectId.ToString(), "project overview", new { projectId },
            () => svc.GetProjectOverviewAsync(projectId));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "record_change"), Description("Record a development change through the git-backed ledger: appends an entry, makes a scoped commit, and returns the change record. Provide rationale, touched files/nodes, test deltas, and verification flags so agentic debt is scored accurately.")]
    public static Task<string> RecordChange(IPlaceContextService svc, IToolCallLog log,
        Guid projectId, string authorName, bool isAgent, string? rationale,
        string[] touchedFiles, string[] touchedNodes,
        int testsAdded, bool architectureReviewerRun, bool liveVerified, string commitMessage)
    {
        var cmd = new RecordChangeCommand(
            projectId, authorName, isAgent, rationale, touchedFiles, touchedNodes,
            testsAdded, 0, 0, 0, 0, architectureReviewerRun, liveVerified, commitMessage);
        return Traced(log, "record_change", projectId.ToString(), commitMessage,
            new { projectId, authorName, isAgent, rationale, touchedFiles, testsAdded, architectureReviewerRun, liveVerified },
            () => svc.RecordChangeAsync(cmd));
    }

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "recompute_debt"), Description("Recompute a project's technical and agentic debt and return the dashboard.")]
    public static Task<string> RecomputeDebt(IPlaceContextService svc, IToolCallLog log, Guid projectId)
        => Traced(log, "recompute_debt", projectId.ToString(), "recompute debt", new { projectId },
            () => svc.RecomputeDebtAsync(projectId));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "get_timeline"), Description("Get the recent change timeline for a project.")]
    public static Task<string> GetTimeline(IPlaceContextService svc, IToolCallLog log, Guid projectId, int take = 20)
        => Traced(log, "get_timeline", projectId.ToString(), "change timeline", new { projectId, take },
            () => svc.GetTimelineAsync(projectId, take));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "add_decision"), Description("Record an architecture decision (ADR-lite) for a project.")]
    public static Task<string> AddDecision(IPlaceContextService svc, IToolCallLog log,
        Guid projectId, string question, string choice, string? rationale)
        => Traced(log, "add_decision", projectId.ToString(), question, new { projectId, question, choice, rationale },
            () => svc.AddDecisionAsync(projectId, question, choice, rationale));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "query_graph"), Description("Ask the project's decision tree a structured question (e.g. 'hotspots', 'decisions', 'unverified', 'activity'). Answered in-process from logged activity.")]
    public static Task<string> QueryGraph(IPlaceContextService svc, IToolCallLog log, Guid projectId, string question)
        => Traced(log, "query_graph", projectId.ToString(), question, new { projectId, question },
            () => svc.QueryGraphAsync(projectId, question));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "add_context"), Description("Append a Markdown section to the project's context document — the durable knowledge agents read before working. Creates the document if absent.")]
    public static Task<string> AddContext(IPlaceContextService svc, IToolCallLog log,
        Guid projectId, [Description("Markdown to append (a heading + notes)")] string section)
        => Traced(log, "add_context", projectId.ToString(), "append context", new { projectId, section },
            () => svc.AddContextAsync(projectId, section));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "set_context"), Description("Replace the project's entire Markdown context document. Use add_context to append a section; use this to rewrite the whole document (e.g. after consolidating notes).")]
    public static Task<string> SetContext(IPlaceContextService svc, IToolCallLog log,
        Guid projectId, [Description("The full Markdown document to store, replacing any existing content")] string markdown)
        => Traced(log, "set_context", projectId.ToString(), "set context", new { projectId, markdown },
            () => svc.SetContextAsync(projectId, markdown));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "get_context"), Description("Fetch the project's Markdown context document. Call this at the start of a session to load what's known about the project.")]
    public static Task<string> GetContext(IPlaceContextService svc, IToolCallLog log, Guid projectId)
        => Traced(log, "get_context", projectId.ToString(), "fetch context", new { projectId },
            () => svc.GetContextAsync(projectId));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "suggest_improvements"), Description("Suggest prioritized improvements for a project, derived from logged activity: churn hotspots, unverified changes, missing context, and debt signals.")]
    public static Task<string> SuggestImprovements(IPlaceContextService svc, IToolCallLog log, Guid projectId)
        => Traced(log, "suggest_improvements", projectId.ToString(), "suggest improvements", new { projectId },
            () => svc.SuggestImprovementsAsync(projectId));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "scaffold_skill"), Description("Scaffold a Claude Code skill into the project (.claude/skills/<name>/SKILL.md), seeded from its recorded decisions and context.")]
    public static Task<string> ScaffoldSkill(IPlaceContextService svc, IToolCallLog log,
        Guid projectId, string skillName, string? description = null)
        => Traced(log, "scaffold_skill", projectId.ToString(), $"scaffold {skillName}", new { projectId, skillName, description },
            () => svc.ScaffoldSkillAsync(projectId, skillName, description));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "record_usage"), Description("Record LLM token usage for a project (metadata only — model name and token counts, never code or prompts). Powers the cost dashboards. Returns the entry with its computed USD cost.")]
    public static Task<string> RecordUsage(IPlaceContextService svc, IToolCallLog log,
        Guid projectId,
        [Description("Model id, e.g. claude-opus-4-8 or claude-sonnet-4-6")] string model,
        [Description("Input (prompt) tokens consumed")] long inputTokens,
        [Description("Output (completion) tokens generated")] long outputTokens,
        [Description("Optional label, e.g. 'review pass' — never code or prompt content")] string? description = null)
        => Traced(log, "record_usage", projectId.ToString(), $"usage {model}", new { projectId, model, inputTokens, outputTokens, description },
            () => svc.RecordUsageAsync(projectId, model, inputTokens, outputTokens, description));

    /// <summary>Times a tool call, serializes its result, and records the trace for the Inspector.</summary>
    private static async Task<string> Traced<T>(
        IToolCallLog log, string tool, string project, string summary, object request, Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action();
            sw.Stop();
            var response = JsonSerializer.Serialize(result, Json);
            log.Record(new ToolCallEntry(
                NewId(), tool, "agent → mcp", project, summary, ToolCallStatus.Ok, sw.ElapsedMilliseconds,
                JsonSerializer.Serialize(request, Json), response, DateTimeOffset.UtcNow));
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            log.Record(new ToolCallEntry(
                NewId(), tool, "agent → mcp", project, summary, ToolCallStatus.Error, sw.ElapsedMilliseconds,
                JsonSerializer.Serialize(request, Json),
                JsonSerializer.Serialize(new { ok = false, error = ex.Message }, Json), DateTimeOffset.UtcNow));
            throw;
        }
    }

    private static string NewId() => "tc-" + Guid.NewGuid().ToString("N")[..8];
}
