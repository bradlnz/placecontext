using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class SetupHermesHandler : ICommandHandler<SetupHermesCommand, SkillScaffoldView>
{
    private readonly IProjectRepository _projects;
    private readonly ISkillScaffolder _scaffolder;

    public SetupHermesHandler(IProjectRepository projects, ISkillScaffolder scaffolder)
    {
        _projects = projects;
        _scaffolder = scaffolder;
    }

    public async Task<SkillScaffoldView> HandleAsync(SetupHermesCommand command, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdAsync(ProjectId.From(command.ProjectId), ct)
            ?? throw new InvalidOperationException($"Project {command.ProjectId} not found.");

        var markdown = BuildMarkdown(project.Name.Value, project.Id.Value);
        var path = await _scaffolder.ScaffoldAsync(project.Path, "hermes", markdown, ct);
        return new SkillScaffoldView("hermes", path, markdown);
    }

    private static string BuildMarkdown(string projectName, Guid projectId) => $$"""
        ---
        name: hermes
        description: Orchestrate PlaceContext jobs for this project over MCP — author, upload, run, monitor, collect artifacts, and automate with schedules and events. Use whenever the task involves creating or running a data/compute job, checking a run's results, or wiring up recurring/event-driven execution.
        ---

        # Hermes — PlaceContext job orchestration

        This project lives in PlaceContext as **{{projectName}}** (`projectId: {{projectId}}`).
        Jobs are map/reduce code workloads that run as isolated containers; their stdout is the
        artifact. Everything below is done with the `placecontext` MCP tools.

        ## 1. Author

        ALWAYS call `job_authoring_guide` BEFORE writing job code — it returns the sandbox contract
        (input arrives on stdin as JSON, the entrypoint lives under /work, stdout is the artifact,
        exit 0 = success) and the available runtimes (python, node, go, ruby, dotnet).

        ## 2. Upload

        `upload_job_code` with `projectId` + `jobName` creates the job on first upload (one `{}`
        shard, concurrency 1) or replaces the file set of an existing one. `filesJson` is
        `[{"path":"main.py","content":"..."}]`; pass `entrypoint` when there is more than one file.
        Env, shards, concurrency, and the reduce step survive re-uploads.

        ## 3. Run and monitor

        - `run_job` runs now and waits; pass `inputPayload` (JSON) to run one shard with parameters.
        - `list_job_runs` → run history; `get_job_run` → one run's shard exit codes, logs, artifacts,
          and the reduce result. A failed shard's log is where to look first.
        - Post-job actions (HTML report / chart / CSV) attach stored artifacts to the run; they are
          listed on the run detail and rendered in the portal.

        ## 4. Automate

        - `create_trigger` with `kind='Schedule'` + `cron` (5-field, workspace timezone) for
          recurring runs; `kind='Event'` + `eventName` to run on an event.
        - `define_event_type` / `emit_event` publish custom events (external systems can POST to
          `/ingest/{eventName}`); built-ins like `job.completed` and `sms.received` also fire triggers.
        - `list_triggers` / `set_trigger_enabled` / `delete_trigger` manage what's armed.

        ## 5. Record what you did

        After orchestration work that matters, `record_activity` with a summary and the touched
        files — the ledger feeds the project's graph, risk, and reports.

        ## Ground rules

        - Secrets go in the project Vault (portal → Vault tab), never in job code or env literals.
        - Jobs have no network egress unless the job explicitly allows it — expect DNS failures
          otherwise, and prefer keeping it off.
        - Prefer one job per concern; shards are for fan-out over inputs, not for unrelated steps.
        """;
}
