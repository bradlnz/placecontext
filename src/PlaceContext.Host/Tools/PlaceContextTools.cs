using System.ComponentModel;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using PlaceContext.Application.Agents;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Tools;

/// <summary>
/// The MCP tool surface an AI agent calls. Each tool is a thin delegate to
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

    [Authorize(Policy = Permission.ProjectsManage)]
    [McpServerTool(Name = "create_project"), Description("Register a project with PlaceContext by its absolute path. Idempotent: re-creating a known path returns the existing project. New projects are created already registered.")]
    public static Task<string> CreateProject(IPlaceContextService svc, IToolCallLog log,
        [Description("Absolute path of the project repo, e.g. /home/brad/code/myapp")] string path,
        [Description("Optional display name; defaults to the last path segment")] string? name = null)
        => Traced(log, "create_project", "—", $"create {path}", new { path, name },
            () => svc.CreateProjectAsync(path, name));

    [Authorize(Policy = Permission.ProjectsManage)]
    [McpServerTool(Name = "onboard"), Description("Bootstrap a project into PlaceContext in one call: create the project, backfill the activity log from git history (when it is a git repo), seed context from any README/AGENTS/CLAUDE docs, and scaffold a local skill + agent for the target AI agent. Returns a setup summary.")]
    public static Task<string> Onboard(IPlaceContextService svc, IToolCallLog log,
        [Description("Absolute path of the project repo")] string path,
        [Description("Optional display name; defaults to the last path segment")] string? name = null,
        [Description("Target AI agent for the scaffolded skill/agent: 'claude' or 'codex'")] string agent = "claude",
        [Description("How many recent commits to backfill into the ledger")] int backfillLimit = 50)
        => Traced(log, "onboard", "—", $"onboard {path}", new { path, name, agent, backfillLimit },
            () => svc.OnboardAsync(path, name, agent, backfillLimit));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "list_projects"), Description("List all projects PlaceContext is tracking.")]
    public static Task<string> ListProjects(IPlaceContextService svc, IToolCallLog log)
        => Traced(log, "list_projects", "—", "list all projects", new { },
            () => svc.GetProjectsAsync());

    [Authorize(Policy = Permission.ProjectsManage)]
    [McpServerTool(Name = "register_project"), Description("Promote a discovered project to registered (watched) status.")]
    public static Task<string> RegisterProject(IPlaceContextService svc, IToolCallLog log,
        [Description("The project's GUID id")] Guid projectId)
        => Traced(log, "register_project", projectId.ToString(), "register project", new { projectId },
            () => svc.RegisterProjectAsync(projectId));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "rebuild_graph"), Description("Rebuild a project's knowledge graph from logged activity (decisions, changes, tool calls) and record the snapshot.")]
    public static Task<string> RebuildGraph(IPlaceContextService svc, IToolCallLog log, Guid projectId, bool incremental = true)
        => Traced(log, "rebuild_graph", projectId.ToString(), "rebuild knowledge graph", new { projectId, incremental },
            () => svc.RebuildGraphAsync(projectId, incremental));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "get_project_overview"), Description("Get a project's overview: status, graph stats, god nodes, and change count.")]
    public static Task<string> GetProjectOverview(IPlaceContextService svc, IToolCallLog log, Guid projectId)
        => Traced(log, "get_project_overview", projectId.ToString(), "project overview", new { projectId },
            () => svc.GetProjectOverviewAsync(projectId));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "record_activity"), Description("Record a change into the activity log: appends an entry, makes a scoped git commit when the project is a git repo, and returns the activity record. Provide rationale, touched items/nodes, check deltas, and verification flags so the change is fully attested.")]
    public static Task<string> RecordActivity(IPlaceContextService svc, IToolCallLog log,
        Guid projectId, string authorName, bool isAgent, string? rationale,
        string[] touchedFiles, string[] touchedNodes,
        int testsAdded, bool architectureReviewerRun, bool liveVerified, string commitMessage)
    {
        var cmd = new RecordActivityCommand(
            projectId, authorName, isAgent, rationale, touchedFiles, touchedNodes,
            testsAdded, 0, 0, architectureReviewerRun, liveVerified, commitMessage);
        return Traced(log, "record_activity", projectId.ToString(), commitMessage,
            new { projectId, authorName, isAgent, rationale, touchedFiles, testsAdded, architectureReviewerRun, liveVerified },
            () => svc.RecordActivityAsync(cmd));
    }

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
    [McpServerTool(Name = AgentToolNames.QueryGraph), Description("Ask the project's knowledge graph a structured question (e.g. 'hotspots', 'decisions', 'unverified', 'activity'). Answered in-process from logged activity.")]
    public static Task<string> QueryGraph(IPlaceContextService svc, IToolCallLog log, Guid projectId, string question)
        => Traced(log, AgentToolNames.QueryGraph, projectId.ToString(), question, new { projectId, question },
            () => svc.QueryGraphAsync(projectId, question));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "suggest_improvements"), Description("Suggest prioritized improvements for a project, derived from logged activity: churn hotspots and unverified changes.")]
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
    [McpServerTool(Name = "setup_hermes"), Description("Install the 'hermes' job-orchestration skill into the project (.claude/skills/hermes/SKILL.md): a playbook that teaches an agent the full PlaceContext job loop — job_authoring_guide → upload_job_code → run_job → list_job_runs/get_job_run → triggers and events. Call once per project (re-running refreshes the skill). Returns the skill's path and content.")]
    public static Task<string> SetupHermes(IPlaceContextService svc, IToolCallLog log, Guid projectId)
        => Traced(log, "setup_hermes", projectId.ToString(), "install hermes skill", new { projectId },
            () => svc.SetupHermesAsync(projectId));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "job_authoring_guide"), Description("Return instructions for structuring PlaceContext job code: the sandbox contract (stdin input, /work entrypoint, stdout output, exit codes), available runtimes, how environment variables/secrets are provided, and the next step (upload_job_code). Call this BEFORE writing a job so the code matches the runtime contract.")]
    public static string JobAuthoringGuide() => JobGuide;

    private const string JobGuide = """
        # Authoring a PlaceContext job

        A job is **map** code (optionally a **reduce** step) run as isolated, sandboxed containers.

        ## The sandbox contract
        - **Input** arrives on **STDIN**: the shard's input payload (one run per payload in the job's
          `inputPayloads`). Read all of stdin and parse it (usually JSON). With no payloads you get one
          run with `{}`.
        - **Working dir**: your files are mounted **read-only at `/work`**; the entrypoint is invoked as
          `/work/<entrypoint>`.
        - **Output**: write your result to **STDOUT** — it's captured as the shard's artifact. Keep logs
          on STDERR so they don't pollute the result.
        - **Exit codes**: `0` = success (configurable). Non-success codes can be mapped to Partial.
        - **No network** by default (sealed sandbox). Set the job's network-egress policy to allow it.

        ## Runtimes
        **Default to `python` unless the user asks for another language** — it reads best for the
        data-shaping work jobs do, and its stdlib covers json/csv/dates without dependencies.
        - `python`→ base `python:3.12-slim`, default entrypoint `main.py`, invoked `python /work/main.py`. **(default)**
        - `node`  → base `node:22-slim`, default entrypoint `index.js`, invoked `node /work/index.js`.
        - `go`    → base `golang:1.23-alpine`, default entrypoint `main.go`, invoked `go run /work/main.go`.
        - `ruby`  → base `ruby:3.3-slim`, default entrypoint `main.rb`, invoked `ruby /work/main.rb`.
        - `dotnet`→ base `mcr.microsoft.com/dotnet/sdk:10.0`, default entrypoint `main.cs`, invoked `dotnet run /work/main.cs` (.NET 10 file-based app).

        ## Dependencies
        Ship your runtime's manifest as an extra file and packages install before the entrypoint runs:
        `requirements.txt` (pip), `package.json` (npm), `Gemfile` (bundler), `go.mod` (go modules).
        Downloads need the job's network-egress policy set to allow — the sealed sandbox is never
        relaxed implicitly. `dotnet` has no manifest step; stay dependency-free there.

        ## Environment variables & secrets
        Plain config goes in the job's `env`. **Secrets/credentials come from the encrypted vault** —
        reference them by name (managed in the portal; encrypted at rest). They are injected as env vars
        into the sandbox at run time; never hard-code credentials in the code.

        ## The artifact IS the point
        Jobs exist to generate artifacts. Emit **JSON** on stdout, and when the result is a numeric
        series (e.g. `[{"day":"mon","total":12}, …]` or `{"mon":12,"tue":31}`) the portal and TUI
        chart it automatically — in the run detail and the global Reports view.

        ## Files & binary artifacts (PDFs, images, CSVs)
        Two channels, both binary-safe end to end:
        - **Write files to `/out`** — every file is captured as a named artifact.
        - **Embed them in your stdout JSON** — `"artifacts": [{"filename": "report.pdf",
          "base64": "…"}]` (use `"content"` for text files). Use this when writing files isn't an
          option (e.g. image workloads in-cluster).
        Emitted PDFs, HTML, and images publish automatically as openable portal links on the run.

        ## Examples
        python `main.py` (the default runtime):
        ```py
        import sys, json, os
        data = json.loads(sys.stdin.read() or "{}")
        api_key = os.environ.get("API_KEY")            # from the vault
        print(json.dumps({"count": len(data.get("items", []))}))
        ```
        node `index.js`:
        ```js
        const input = JSON.parse(require('fs').readFileSync(0, 'utf8') || '{}');
        const apiKey = process.env.API_KEY;            // from the vault
        const result = { count: (input.items ?? []).length };
        process.stdout.write(JSON.stringify(result));  // the artifact
        ```

        ## Next step
        Upload with **upload_job_code**: `filesJson` = [{"path":"main.py","content":"…"}], `runtimeId`
        (defaults to "python"; also "node"|"go"|"ruby"|"dotnet"), and `entrypoint` when uploading
        multiple files.
        """;

    [Authorize(Policy = Permission.JobsEdit)]
    [McpServerTool(Name = "upload_job_code"), Description("Upload (replace) the source file set of a job's map step so it can be run as isolated containers. Target an existing job by jobId, OR by projectId + jobName (the job is created with sensible defaults — one '{}' shard, concurrency 1, success exit 0 — when it does not yet exist). 'filesJson' is a JSON array of {\"path\":\"index.js\",\"content\":\"...\"}; paths may include subdirectories (e.g. 'lib/report.js'). 'runtimeId' selects the sandbox ('python' — the default — 'node', 'go', 'ruby', or 'dotnet'); 'entrypoint' is the path of the file to invoke (required when uploading more than one file; defaults to the runtime's default for a single file). Existing input payloads, env, concurrency, reduce step, and exit-code policy are preserved.")]
    public static Task<string> UploadJobCode(IPlaceContextService svc, IToolCallLog log,
        [Description("JSON array of files, e.g. [{\"path\":\"main.py\",\"content\":\"...\"}]")] string filesJson,
        [Description("Runtime sandbox id: 'python' (default), 'node', 'go', 'ruby', or 'dotnet'")] string runtimeId = "python",
        [Description("Existing job id to target; omit to target by projectId + jobName")] Guid? jobId = null,
        [Description("Project id — required when jobId is omitted")] Guid? projectId = null,
        [Description("Job name to find or create within the project — required when jobId is omitted")] string? jobName = null,
        [Description("Entry-point file path; required for multi-file uploads, defaults to the runtime default for a single file")] string? entrypoint = null)
    {
        var files = ParseFiles(filesJson);
        var cmd = new UploadJobCodeCommand(jobId, projectId, jobName, runtimeId, entrypoint, files);
        return Traced(log, "upload_job_code",
            projectId?.ToString() ?? jobId?.ToString() ?? "—",
            $"upload {files.Count} file(s) → {jobName ?? jobId?.ToString() ?? "job"}",
            // Log metadata only — never the file contents.
            new { jobId, projectId, jobName, runtimeId, entrypoint, fileCount = files.Count, paths = files.Select(f => f.Path) },
            () => svc.UploadJobCodeAsync(cmd));
    }

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = AgentToolNames.ListJobs), Description("List a project's jobs (map/reduce code workloads) with their run configuration: shard count, concurrency, runtime, and network-egress policy. Use this to discover jobs and their ids before running one.")]
    public static Task<string> ListJobs(IPlaceContextService svc, IToolCallLog log, Guid projectId)
        => Traced(log, AgentToolNames.ListJobs, projectId.ToString(), "list jobs", new { projectId },
            () => svc.ListJobsAsync(projectId));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "get_job"), Description("Get one job's full definition by id, including its map/reduce source files, input payloads (shards), env, concurrency, and exit-code policy. Returns null if the job does not exist.")]
    public static Task<string> GetJob(IPlaceContextService svc, IToolCallLog log, Guid jobId)
        => Traced(log, "get_job", jobId.ToString(), "get job", new { jobId },
            () => svc.GetJobAsync(jobId));

    [Authorize(Policy = Permission.JobsRun)]
    [McpServerTool(Name = AgentToolNames.RunJob), Description("Run a job now: executes its map shards (and reduce step, if defined) as isolated containers and waits for completion. Returns the run detail — overall status plus each shard's exit code, outcome, artifact, and log, and any reduce result. Pass 'inputPayload' to override the stored shards with a single shard carrying that payload (e.g. parameters for a job that declares input fields). Use list_job_runs/get_job_run to fetch results later.")]
    public static Task<string> RunJob(IPlaceContextService svc, IToolCallLog log, Guid jobId,
        [Description("Optional input payload override (typically JSON); runs a single shard with it")] string? inputPayload = null)
        => Traced(log, AgentToolNames.RunJob, jobId.ToString(), "run job", new { jobId, inputPayload },
            () => svc.RunJobAsync(jobId, inputPayload));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = AgentToolNames.ListJobRuns), Description("List a job's run history (most recent first): each run's status, start/finish times, and shard success/partial/failure counts. Use get_job_run for a run's full artifacts.")]
    public static Task<string> ListJobRuns(IPlaceContextService svc, IToolCallLog log, Guid jobId)
        => Traced(log, AgentToolNames.ListJobRuns, jobId.ToString(), "list job runs", new { jobId },
            () => svc.ListJobRunsAsync(jobId));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "get_job_run"), Description("Get the full result of a single job run by run id: overall status, every shard's exit code/outcome/artifact/log, the reduce result, and a snapshot of the executed workload spec. Returns null if the run does not exist.")]
    public static Task<string> GetJobRun(IPlaceContextService svc, IToolCallLog log, Guid runId)
        => Traced(log, "get_job_run", runId.ToString(), "get job run", new { runId },
            () => svc.GetJobRunAsync(runId));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = AgentToolNames.GetArtifacts), Description("List recent artifacts (reports, charts, CSVs) produced by job runs; returns metadata and download URLs, not file contents")]
    public static Task<string> GetArtifacts(IPlaceContextService svc, IToolCallLog log,
        [Description("Project id")] Guid projectId,
        [Description("Max artifacts to return (newest first)")] int take = 100)
        => Traced(log, AgentToolNames.GetArtifacts, projectId.ToString(), "list artifacts", new { projectId, take },
            async () => (await svc.ListProjectArtifactsAsync(projectId, take))
                .Select(a => new
                {
                    id = a.Id,
                    runId = a.RunId,
                    jobId = a.JobId,
                    kind = a.Kind,
                    title = a.Title,
                    contentType = a.ContentType,
                    sizeBytes = a.SizeBytes,
                    createdAt = a.CreatedAt,
                    downloadUrl = $"/runs/{a.RunId}/artifacts/{a.Id}",
                })
                .ToList());

    // ── Job chains ────────────────────────────────────────────────────────────────────────────────

    [Authorize(Policy = Permission.ChainsManage)]
    [McpServerTool(Name = "create_job_chain"), Description("Define a job chain: an ordered pipeline of stages of existing jobs, where each stage's primary output becomes the next stage's stdin input payload. 'jobIdsJson' is a JSON array where each element is either a single job id (an ordinary sequential stage) or a JSON array of job ids (a stage that fans out — every job in it runs in parallel; the NEXT stage is the join and receives every branch's output as a JSON array). Example: [\"<guidA>\", [\"<guidB1>\",\"<guidB2>\"], \"<guidJoin>\"] runs A, then B1+B2 in parallel, then Join once both finish — Join fails the whole chain if either B1 or B2 fails. A plain flat array like [\"<guid>\",\"<guid>\"] is an ordinary linear chain (every stage size 1). The same job id may appear more than once. All jobs must belong to the project. Use run_job_chain to execute it. Optional 'gatesJson' lets you attach flow-control gates between stages: a JSON array of objects (or null entries) parallel to the stages — {\"type\":\"wait\",\"duration\":30} pauses before that stage; {\"type\":\"condition\",\"expression\":\"exists:data.value\"} skips the stage when the condition is false.")]
    public static Task<string> CreateJobChain(IPlaceContextService svc, IToolCallLog log,
        Guid projectId, string name,
        [Description("JSON array of job ids and/or job-id arrays (for a parallel fan-out stage), in run order")] string jobIdsJson,
        string? description = null,
        [Description("Optional JSON array of gate objects (or null) — one per stage, in order. null = no gate. Example: [null, {\"type\":\"wait\",\"duration\":30}, {\"type\":\"condition\",\"expression\":\"exists:data.value\"}]")] string? gatesJson = null)
    {
        var stages = ParseChainStages(jobIdsJson);
        var flat = stages.SelectMany(s => s).ToList();
        var gates = gatesJson is not null ? ParseChainGates(gatesJson, stages.Count) : null;
        return Traced(log, "create_job_chain", projectId.ToString(),
            $"create chain '{name}' ({stages.Count} stage(s), {flat.Count} step(s))", new { projectId, name, stages, description, gates },
            () => svc.CreateJobChainAsync(projectId, name, description, flat, stages, gates));
    }

    [Authorize(Policy = Permission.ChainsManage)]
    [McpServerTool(Name = "update_job_chain"), Description("Replace a job chain's name, description, and stages. 'jobIdsJson' follows the same shape as create_job_chain: a flat array of job ids for a linear chain, or a mix of single ids and job-id arrays to author fan-out stages. Optional 'gatesJson' replaces gates in the same format as create_job_chain.")]
    public static Task<string> UpdateJobChain(IPlaceContextService svc, IToolCallLog log,
        Guid chainId, string name,
        [Description("JSON array of job ids and/or job-id arrays (for a parallel fan-out stage), in run order")] string jobIdsJson,
        string? description = null,
        [Description("Optional JSON array of gate objects (or null) — one per stage, in order. null = no gate.")] string? gatesJson = null)
    {
        var stages = ParseChainStages(jobIdsJson);
        var flat = stages.SelectMany(s => s).ToList();
        var gates = gatesJson is not null ? ParseChainGates(gatesJson, stages.Count) : null;
        return Traced(log, "update_job_chain", chainId.ToString(),
            $"update chain '{name}' ({stages.Count} stage(s), {flat.Count} step(s))", new { chainId, name, stages, description, gates },
            () => svc.UpdateJobChainAsync(chainId, name, description, flat, stages, gates));
    }

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "list_job_chains"), Description("List a project's job chains with their stages (job ids + names; a stage with more than one job is a parallel fan-out group). Use this to discover chain ids before running one.")]
    public static Task<string> ListJobChains(IPlaceContextService svc, IToolCallLog log, Guid projectId)
        => Traced(log, "list_job_chains", projectId.ToString(), "list job chains", new { projectId },
            () => svc.ListJobChainsAsync(projectId));

    [Authorize(Policy = Permission.JobsRun)]
    [McpServerTool(Name = AgentToolNames.RunJobChain), Description("Run a job chain now: executes every stage in order, waiting for completion. A stage with more than one job runs them all in parallel (a fan-out group) and is all-or-nothing — it only advances once every job in it finishes, and if any of them fails the whole chain fails and every later stage (including the join that would follow the fan-out) is skipped; a Partial job continues but downgrades the chain status. Each stage's primary output feeds the next stage's input (a fan-out group's branches are combined into one JSON array for the join). Pass 'inputPayload' to feed the FIRST stage; omit it to run the first job with its stored shard payloads. Returns the chain status, each executed step's run id + status (fetch full artifacts with get_job_run), and the final output — the last stage's output, i.e. the chain's result.")]
    public static Task<string> RunJobChain(IPlaceContextService svc, IToolCallLog log, Guid chainId,
        [Description("Optional input payload for the first step (typically JSON)")] string? inputPayload = null)
        => Traced(log, AgentToolNames.RunJobChain, chainId.ToString(), "run job chain", new { chainId, inputPayload },
            () => svc.RunJobChainAsync(chainId, inputPayload));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "delete_job_chain"), Description("Permanently remove a job chain definition (the jobs and their run history are untouched). Returns true if it existed.")]
    public static Task<string> DeleteJobChain(IPlaceContextService svc, IToolCallLog log, Guid chainId)
        => Traced(log, "delete_job_chain", chainId.ToString(), "delete job chain", new { chainId },
            () => svc.DeleteJobChainAsync(chainId));

    [Authorize(Policy = Permission.JobsRun)]
    [McpServerTool(Name = "replay_job_chain"), Description("Replay a failed/partial chain run from the point of failure. The original run is preserved; a new run is created that re-executes from the first failed step (or a specific step index). Use this to retry a chain after fixing a failing job, without re-running steps that already succeeded. Returns the new chain run status and step details.")]
    public static Task<string> ReplayJobChain(IPlaceContextService svc, IToolCallLog log, Guid chainId, Guid originalRunId,
        [Description("Optional: 0-based step index to resume from (default: first failed step)")] int? fromStepIndex = null,
        [Description("Optional: input payload override for the replay start step")] string? inputPayload = null)
        => Traced(log, "replay_job_chain", chainId.ToString(), "replay job chain", new { chainId, originalRunId, fromStepIndex, inputPayload },
            () => svc.ReplayJobChainAsync(new ReplayJobChainCommand(chainId, originalRunId, fromStepIndex, inputPayload)));

    /// <summary>
    /// Parses a chain's stages from the tool's 'jobIdsJson' argument: each top-level element is
    /// either a job id string (its own size-1, ordinary sequential stage) or a JSON array of job id
    /// strings (a parallel fan-out stage). A purely flat array — every existing caller's shape — is
    /// therefore exactly a chain whose every stage is size 1, unchanged from before fan-out existed.
    /// </summary>
    private static List<List<Guid>> ParseChainStages(string jobIdsJson)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(jobIdsJson).RootElement;
        }
        catch (JsonException e)
        {
            throw new ArgumentException(
                $"jobIdsJson must be a JSON array of job ids (guids), optionally with nested arrays for a parallel fan-out stage: {e.Message}");
        }
        if (root.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("jobIdsJson must be a JSON array.");

        var stages = new List<List<Guid>>();
        foreach (var element in root.EnumerateArray())
        {
            stages.Add(element.ValueKind == JsonValueKind.Array
                ? element.EnumerateArray().Select(ParseJobIdElement).ToList()
                : new List<Guid> { ParseJobIdElement(element) });
        }
        return stages;
    }

    private static List<ChainGate?>? ParseChainGates(string gatesJson, int stageCount)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(gatesJson).RootElement; }
        catch (JsonException e)
        {
            throw new ArgumentException($"gatesJson must be a JSON array: {e.Message}");
        }
        if (root.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("gatesJson must be a JSON array.");

        var gates = new List<ChainGate?>();
        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Null)
            {
                gates.Add(null);
                continue;
            }
            if (element.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Each gate entry must be a JSON object or null.");

            if (!element.TryGetProperty("type", out var typeProp))
                throw new ArgumentException("Each gate object must have a 'type' property ('wait' or 'condition').");

            var type = typeProp.GetString();
            gates.Add(type switch
            {
                "wait" => element.TryGetProperty("duration", out var durProp)
                    ? new WaitGate(TimeSpan.FromSeconds(durProp.GetDouble()))
                    : new WaitGate(TimeSpan.FromSeconds(30)),
                "condition" => element.TryGetProperty("expression", out var exprProp)
                    ? new ConditionGate(exprProp.GetString() ?? "exists:data")
                    : new ConditionGate("exists:data"),
                _ => throw new ArgumentException($"Unknown gate type '{type}'. Use 'wait' or 'condition'."),
            });
        }

        if (gates.Count > stageCount)
            gates = gates.Take(stageCount).ToList();
        else if (gates.Count < stageCount)
        {
            // Pad with null for stages beyond the gates array
            var pad = Enumerable.Repeat((ChainGate?)null, stageCount - gates.Count);
            gates.AddRange(pad);
        }

        return gates;
    }

    private static Guid ParseJobIdElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var id))
            return id;
        throw new ArgumentException("jobIdsJson entries (and fan-out stage entries) must be job id guids, as strings.");
    }

    [Authorize(Policy = Permission.DataRead)]
    [McpServerTool(Name = "query_project_data"), Description("Run a read-only SELECT against the project's own database and get columns + rows back. STRICTLY SELECT-ONLY: any other statement kind, multiple statements, or write/DDL keywords are rejected. The query executes as the project's isolated database role — it can only ever see this project's tables. Use list_project_tables/table names from the Data tab to explore, then build informative SQL charts with save_sql_chart.")]
    public static Task<string> QueryProjectData(IPlaceContextService svc, IToolCallLog log,
        [Description("The project whose database to query")] Guid projectId,
        [Description("A single SELECT statement (no semicolons, no writes)")] string sql)
        => Traced(log, "query_project_data", projectId.ToString(), Truncate(sql, 120), new { projectId, sql },
            async () =>
            {
                EnsureSelectOnly(sql);
                var result = await svc.ExecuteProjectDataAsync(projectId, sql);
                return new
                {
                    result.Columns,
                    Rows = result.Rows.Take(200).ToList(),
                    result.Truncated,
                    capped = result.Rows.Count > 200,
                };
            });

    [Authorize(Policy = Permission.DataWrite)]
    [McpServerTool(Name = "save_sql_chart"), Description("Create or update a named SQL chart on the project's Analytics tab (also shown read-only on the Dashboard). The SELECT runs isolated inside the project's database; its first text column becomes the labels and numeric columns become series, rendered as the given chart type. STRICTLY SELECT-ONLY — writes and DDL are rejected. Re-saving the same name replaces the chart, so agents can iterate.")]
    public static Task<string> SaveSqlChart(IPlaceContextService svc, IToolCallLog log,
        [Description("The project the chart belongs to")] Guid projectId,
        [Description("Chart name (re-save the same name to update)")] string name,
        [Description("A single SELECT statement shaping labels + numeric series")] string sql,
        [Description("'bar', 'line', or 'pie'")] string chartType = "bar")
        => Traced(log, "save_sql_chart", projectId.ToString(), name, new { projectId, name, sql, chartType },
            async () =>
            {
                EnsureSelectOnly(sql);
                return await svc.SaveSqlChartAsync(projectId, name, sql, chartType);
            });

    // SELECT-only gate for the data tools — shared with project views. Belt and braces on top
    // of the per-project role isolation: the role can write its own tables, these tools must not.
    private static void EnsureSelectOnly(string sql)
        => SaveProjectViewHandler.EnsureSelectOnly(sql);

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    // Fine-grained permission gate — settings.manage (Admin/Owner by default; a Member can be granted
    // it via an override). Follow this pattern for a new sensitive tool: [Authorize(Policy = Permission.X)].
    [Authorize(Policy = Permission.SettingsManage)]
    [McpServerTool(Name = "set_workspace_timezone"), Description("Set the workspace's IANA timezone (e.g. 'Australia/Brisbane'). Schedule triggers evaluate their cron expressions in this timezone, and job/schedule times display in it. Agents should set this from the user's locale context before creating schedules.")]
    public static Task<string> SetWorkspaceTimezone(IToolCallLog log,
        PlaceContext.Infrastructure.Tenancy.ITenantStore tenants,
        PlaceContext.Application.Ports.ICurrentTenant tenant,
        [Description("IANA timezone id, e.g. 'Australia/Brisbane' or 'UTC'")] string timeZoneId)
        => Traced(log, "set_workspace_timezone", "", timeZoneId, new { timeZoneId },
            async () =>
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); // throws on unknown ids
                await tenants.SetTimeZoneAsync(tenant.TenantId, timeZoneId);
                return new { timeZoneId, applied = true };
            });

    // ── Triggers ──────────────────────────────────────────────────────────────────────────────────

    [Authorize(Policy = Permission.TriggersManage)]
    [McpServerTool(Name = "create_trigger"), Description("Create a trigger that starts runs of a job automatically. kind='Schedule' fires on a recurring cron expression (5-field, or 6-field with seconds; evaluated in the workspace timezone) — supply 'cron'. kind='Event' fires whenever a named event is emitted — supply 'eventName' (a built-in like 'job.completed'/'activity.recorded', or a user-defined event type). The project is inferred from the job. Firing enqueues an independent run; concurrent runs are allowed.")]
    public static Task<string> CreateTrigger(IPlaceContextService svc, IToolCallLog log,
        [Description("The job to run when the trigger fires")] Guid jobId,
        [Description("Human-readable trigger name")] string name,
        [Description("'Schedule' or 'Event'")] string kind,
        [Description("Cron expression (schedule triggers); e.g. '0 0 * * *' for daily midnight")] string? cron = null,
        [Description("Event name to subscribe to (event triggers)")] string? eventName = null)
        => Traced(log, "create_trigger", jobId.ToString(), $"{kind} trigger {name}",
            new { jobId, name, kind, cron, eventName },
            () => svc.CreateTriggerAsync(new CreateTriggerCommand(jobId, name, kind, cron, eventName)));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "list_triggers"), Description("List a project's triggers (schedule + event), with their next-run/last-fired times and enabled state.")]
    public static Task<string> ListTriggers(IPlaceContextService svc, IToolCallLog log, Guid projectId)
        => Traced(log, "list_triggers", projectId.ToString(), "list triggers", new { projectId },
            () => svc.ListTriggersAsync(projectId));

    [Authorize(Policy = Permission.TriggersManage)]
    [McpServerTool(Name = "set_trigger_enabled"), Description("Enable or pause a trigger. Re-enabling a schedule recomputes its next-run time; pausing stops it firing until re-enabled.")]
    public static Task<string> SetTriggerEnabled(IPlaceContextService svc, IToolCallLog log,
        Guid triggerId, bool enabled)
        => Traced(log, "set_trigger_enabled", triggerId.ToString(), enabled ? "enable trigger" : "pause trigger",
            new { triggerId, enabled },
            () => svc.SetTriggerEnabledAsync(triggerId, enabled));

    [Authorize(Policy = Permission.TriggersManage)]
    [McpServerTool(Name = "delete_trigger"), Description("Permanently remove a trigger. Returns true if it existed.")]
    public static Task<string> DeleteTrigger(IPlaceContextService svc, IToolCallLog log, Guid triggerId)
        => Traced(log, "delete_trigger", triggerId.ToString(), "delete trigger", new { triggerId },
            () => svc.DeleteTriggerAsync(triggerId));

    // ── Events ────────────────────────────────────────────────────────────────────────────────────

    [Authorize(Policy = Permission.EventsManage)]
    [McpServerTool(Name = "define_event_type"), Description("Define (or update) a user event type for this workspace so triggers can subscribe to it and it can be emitted. The name must not collide with a reserved built-in event. 'payloadSchema' is optional freetext/JSON describing the expected payload fields.")]
    public static Task<string> DefineEventType(IPlaceContextService svc, IToolCallLog log,
        [Description("Unique event name, e.g. 'deploy.finished'")] string name,
        [Description("What this event means / when it is emitted")] string? description = null,
        [Description("Optional freetext/JSON describing the payload fields")] string? payloadSchema = null)
        => Traced(log, "define_event_type", "—", $"define {name}", new { name, description, payloadSchema },
            () => svc.DefineEventTypeAsync(name, description, payloadSchema));

    [Authorize(Policy = Permission.EventsManage)]
    [McpServerTool(Name = "emit_event"), Description("Emit an event occurrence. Every enabled event-trigger subscribed to the name fires (each enqueues a job run); the optional payload is passed through as parameters for those runs. The name may be a user-defined event type or a built-in. Returns the occurrence and how many triggers fired.")]
    public static Task<string> EmitEvent(IPlaceContextService svc, IToolCallLog log,
        [Description("Event name to emit")] string name,
        [Description("Optional project this event concerns")] Guid? projectId = null,
        [Description("Optional opaque payload (typically JSON)")] string? payload = null)
        => Traced(log, "emit_event", projectId?.ToString() ?? "—", $"emit {name}", new { name, projectId, payload },
            () => svc.EmitEventAsync(name, projectId, payload));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "list_event_types"), Description("List all event types: the reserved built-ins (job.completed, activity.recorded) plus this workspace's user-defined ones.")]
    public static Task<string> ListEventTypes(IPlaceContextService svc, IToolCallLog log)
        => Traced(log, "list_event_types", "—", "list event types", new { },
            () => svc.ListEventTypesAsync());

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "list_event_occurrences"), Description("List the most recent emitted events (the event log), newest first.")]
    public static Task<string> ListEventOccurrences(IPlaceContextService svc, IToolCallLog log, int take = 50)
        => Traced(log, "list_event_occurrences", "—", "list event log", new { take },
            () => svc.ListEventOccurrencesAsync(take));

    [Authorize(Policy = "Member")]
    [McpServerTool(Name = "search_run_outputs"), Description("Semantic search over a project's job-run outputs: returns the runs whose organized output is most similar to the query text, by vector similarity. Requires embeddings to be configured (Voyage AI); returns an empty list otherwise. Use this to find prior runs related to a question or to surface related results.")]
    public static Task<string> SearchRunOutputs(IPlaceContextService svc, IToolCallLog log,
        Guid projectId, string query, int take = 10)
        => Traced(log, "search_run_outputs", projectId.ToString(), $"search runs: {query}", new { projectId, query, take },
            () => svc.SearchRunOutputsAsync(projectId, query, take));

    // ── Agent chat ────────────────────────────────────────────────────────────────────────────────

    [Authorize(Policy = Permission.AgentsChat)]
    [McpServerTool(Name = "chat_with_agent"), Description("Send a message to the project's chat agent and return the assistant's reply. The agent retrieves relevant context from run outputs and the dependency graph (RAG). Pass a sessionId to continue an existing conversation, or omit it to start a new one.")]
    public static Task<string> ChatWithAgent(IPlaceContextService svc, IToolCallLog log,
        [Description("Project id")] Guid projectId,
        [Description("User message")] string message,
        [Description("Existing session id to continue (omit for new session)")] Guid? sessionId = null)
        => Traced(log, "chat_with_agent", projectId.ToString(), $"chat: {message[..Math.Min(60, message.Length)]}", new { projectId, message, sessionId },
            () => svc.SendAgentMessageAsync(new Application.Features.SendAgentMessageCommand(projectId, sessionId, message)));

    [Authorize(Policy = Permission.AgentsChat)]
    [McpServerTool(Name = "list_agent_sessions"), Description("List all chat sessions for a project (newest first).")]
    public static Task<string> ListAgentSessions(IPlaceContextService svc, IToolCallLog log,
        [Description("Project id")] Guid projectId)
        => Traced(log, "list_agent_sessions", projectId.ToString(), "list chat sessions", new { projectId },
            () => svc.ListAgentChatSessionsAsync(projectId));

    [Authorize(Policy = Permission.AgentsManage)]
    [McpServerTool(Name = "get_agent_config"), Description("Get the chat agent configuration for a project (model, prompt, context settings, enabled flag).")]
    public static Task<string> GetAgentConfig(IPlaceContextService svc, IToolCallLog log,
        [Description("Project id")] Guid projectId)
        => Traced(log, "get_agent_config", projectId.ToString(), "get agent config", new { projectId },
            () => svc.GetAgentConfigAsync(projectId));

    [Authorize(Policy = Permission.AgentsManage)]
    [McpServerTool(Name = "update_agent_config"), Description("Update the chat agent configuration for a project.")]
    public static Task<string> UpdateAgentConfig(IPlaceContextService svc, IToolCallLog log,
        [Description("Project id")] Guid projectId,
        [Description("Model name (e.g. gemma3:4b)")] string baseModel,
        [Description("System prompt")] string systemPrompt,
        [Description("Preamble text prepended before the system prompt")] string preamble,
        [Description("Tool catalog text describing available tools")] string toolCatalog,
        [Description("Launchpad tool catalog text")] string launchpadToolCatalog,
        [Description("Max context chunks from RAG (default 5)")] int maxContextChunks = 5,
        [Description("Temperature 0-2 (default 0.7)")] float temperature = 0.7f,
        [Description("Top-p 0-1 (default 0.9)")] float topP = 0.9f,
        [Description("Whether the agent is enabled")] bool enabled = true)
        => Traced(log, "update_agent_config", projectId.ToString(), $"update agent config: {baseModel}", new { projectId, baseModel, maxContextChunks, temperature, topP, enabled },
            () => svc.UpdateAgentConfigAsync(new Application.Features.UpdateAgentConfigCommand(projectId, baseModel, systemPrompt, preamble, toolCatalog, launchpadToolCatalog, maxContextChunks, temperature, topP, enabled)));

    private static IReadOnlyList<CodeFileDto> ParseFiles(string filesJson)
    {
        if (string.IsNullOrWhiteSpace(filesJson))
            throw new ArgumentException("filesJson must be a non-empty JSON array of {path, content}.");

        List<ToolFileInputDto>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<List<ToolFileInputDto>>(filesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"filesJson is not valid JSON: {ex.Message}");
        }

        if (parsed is null || parsed.Count == 0)
            throw new ArgumentException("filesJson must contain at least one file.");

        return parsed.Select(f =>
        {
            if (string.IsNullOrWhiteSpace(f.Path))
                throw new ArgumentException("Every file must have a non-empty 'path'.");
            return new CodeFileDto(f.Path!, f.Content ?? "");
        }).ToList();
    }

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
