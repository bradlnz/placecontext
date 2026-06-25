using System.Text;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Executes a job: snapshots the current spec onto the run, fans out all map shards (bounded
/// concurrency), optionally runs the reduce step with the shard artifacts mounted as inputs,
/// aggregates status via the job's ExitCodePolicy, and persists the run.
/// Completed run artifacts (opaque JSON) are appended to the project's context document so the
/// existing generic report-generation layer can observe them.
///
/// WorkloadSource resolution — image vs code:
/// • ImageWorkload: passed to the runner via <see cref="WorkloadRunRequest.Image"/>; same behaviour as before.
/// • CodeWorkload: runtimeId, source, and entrypoint are passed; the runner resolves the base image
///   from the runtime registry (in WorkloadRunnerOptions) and materialises source to a temp work dir.
///
/// All workload inputs and outputs are treated as opaque by this handler — no domain knowledge.
/// </summary>
public sealed class RunJobHandler : ICommandHandler<RunJobCommand, JobRunDetailView>
{
    private readonly IJobRepository _jobs;
    private readonly IJobRunRepository _runs;
    private readonly IProjectContextRepository _contexts;
    private readonly IWorkloadRunner _runner;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly EventDispatchService? _events;

    public RunJobHandler(
        IJobRepository jobs,
        IJobRunRepository runs,
        IProjectContextRepository contexts,
        IWorkloadRunner runner,
        IUnitOfWork uow,
        IClock clock,
        // Optional so unit tests can construct the handler without the event layer; DI always supplies it.
        EventDispatchService? events = null)
    {
        _jobs = jobs;
        _runs = runs;
        _contexts = contexts;
        _runner = runner;
        _uow = uow;
        _clock = clock;
        _events = events;
    }

    public async Task<JobRunDetailView> HandleAsync(RunJobCommand command, CancellationToken ct = default)
    {
        var job = await _jobs.GetByIdAsync(command.JobId, ct)
            ?? throw new InvalidOperationException($"Job {command.JobId} not found.");

        // An input override (modal-collected parameters or event-injected fields) replaces the stored
        // shard payloads with a single shard carrying the supplied payload.
        var effectiveMap = command.InputPayload is { } p
            ? new MapSpec(job.MapSpec.Source, new[] { p }, job.MapSpec.Env)
            : job.MapSpec;

        // Snapshot the effective spec onto the run at start-time (captures AllowNetworkEgress too).
        var snapshot = WorkloadSnapshot.From(effectiveMap, job.ReduceSpec, job.ConcurrencyLimit,
            job.AllowNetworkEgress);
        var startedAt = _clock.UtcNow;
        var run = JobRun.Start(job.Id, job.ProjectId, startedAt, snapshot);
        await _runs.AddAsync(run, ct);
        await _uow.SaveChangesAsync(ct);

        // ---- MAP PHASE: fan-out shards with bounded concurrency ----
        var shardResults = await RunMapShardsAsync(job, effectiveMap.InputPayloads, run.Id, ct);

        // ---- REDUCE PHASE (optional) ----
        ReduceResult? reduceResult = null;
        if (job.HasReduceStep && job.ReduceSpec is { } reduceSpec)
        {
            reduceResult = await RunReduceStepAsync(job, run.Id, shardResults, reduceSpec, ct);
        }

        // ---- AGGREGATE + PERSIST ----
        var finishedAt = _clock.UtcNow;
        run.Complete(shardResults, reduceResult, finishedAt);
        await _runs.UpdateAsync(run, ct);

        // Persist a summary of the completed run into the project's context document so the
        // existing generic generation layer (GenerateReportHandler) can see run artifacts.
        await AppendRunSummaryToProjectContextAsync(job, run, ct);

        await _uow.SaveChangesAsync(ct);

        // Raise the built-in "job.completed" domain event so event-triggers can chain off this run.
        if (_events is not null)
        {
            var payload = $"{{\"runId\":\"{run.Id:N}\",\"jobId\":\"{job.Id:N}\",\"status\":\"{run.Status}\"}}";
            await _events.RaiseAsync(BuiltInEvents.JobCompleted, run.ProjectId, payload, ct);
        }

        return JobViewMapper.ToDetailView(run);
    }

    // ---- MAP PHASE ----

    private async Task<List<ShardResult>> RunMapShardsAsync(
        Job job, IReadOnlyList<string> payloads, Guid runId, CancellationToken ct)
    {
        var concurrency = job.ConcurrencyLimit;
        var semaphore = new SemaphoreSlim(concurrency, concurrency);
        var tasks = new Task<ShardResult>[payloads.Count];

        for (var i = 0; i < payloads.Count; i++)
        {
            var index = i;
            var payload = payloads[i];
            tasks[index] = RunShardAsync(job, runId, index, payload, semaphore, ct);
        }

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<ShardResult> RunShardAsync(
        Job job, Guid runId, int index, string payload,
        SemaphoreSlim semaphore, CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        try
        {
            var correlationId = $"{runId:N}-map-{index}";
            var request = BuildRequest(job.MapSpec.Source, payload, job.MapSpec.Env,
                Array.Empty<(string, string)>(), correlationId, job.AllowNetworkEgress);

            var result = await _runner.RunAsync(request, ct);
            var outcome = job.ExitCodePolicy.Classify(result.ExitCode);
            var log = CombineLog(result.Stdout, result.Stderr);

            return new ShardResult(index, result.ExitCode, outcome, result.Artifact, log);
        }
        finally
        {
            semaphore.Release();
        }
    }

    // ---- REDUCE PHASE ----

    private async Task<ReduceResult> RunReduceStepAsync(
        Job job, Guid runId,
        List<ShardResult> shardResults,
        ReduceSpec reduceSpec,
        CancellationToken ct)
    {
        // Build artifact mounts: each shard's artifact content is passed to the runner which
        // materialises them as temp files and mounts them read-only at /in/{index}/result.json.
        var artifactMounts = shardResults
            .Where(s => s.Artifact is not null)
            .Select(s => (s.Artifact!, $"/in/{s.Index}/result.json"))
            .ToList();

        var correlationId = $"{runId:N}-reduce";
        var request = BuildRequest(reduceSpec.Source, "{}", reduceSpec.Env,
            artifactMounts, correlationId, job.AllowNetworkEgress);

        var result = await _runner.RunAsync(request, ct);
        var succeeded = job.ExitCodePolicy.SuccessCodes.Contains(result.ExitCode);
        var log = CombineLog(result.Stdout, result.Stderr);

        return new ReduceResult(result.ExitCode, succeeded, result.Artifact, log);
    }

    // ---- WorkloadSource → WorkloadRunRequest ------------------------------------------------

    /// <summary>
    /// Builds a <see cref="WorkloadRunRequest"/> from a <see cref="WorkloadSource"/>.
    /// For ImageWorkload: sets Image directly.
    /// For CodeWorkload: sets CodeSource/RuntimeId/Entrypoint fields; the runner resolves
    /// the base image from the runtime registry in WorkloadRunnerOptions.
    /// </summary>
    private static WorkloadRunRequest BuildRequest(
        WorkloadSource source,
        string stdinPayload,
        IReadOnlyDictionary<string, string> env,
        IReadOnlyList<(string Content, string ContainerPath)> artifactMounts,
        string correlationId,
        bool allowNetworkEgress)
    {
        return source switch
        {
            WorkloadSource.ImageWorkload img => new WorkloadRunRequest(
                Image: img.Image,
                StdinPayload: stdinPayload,
                Env: env,
                ArtifactMounts: artifactMounts,
                CorrelationId: correlationId,
                CodeFiles: null,
                RuntimeId: null,
                Entrypoint: null,
                AllowNetworkEgress: allowNetworkEgress),

            WorkloadSource.CodeWorkload code => new WorkloadRunRequest(
                Image: null,
                StdinPayload: stdinPayload,
                Env: env,
                ArtifactMounts: artifactMounts,
                CorrelationId: correlationId,
                CodeFiles: code.Files.Select(f => (f.Path, f.Content)).ToList(),
                RuntimeId: code.RuntimeId,
                Entrypoint: code.Entrypoint,
                AllowNetworkEgress: allowNetworkEgress),

            _ => throw new InvalidOperationException($"Unsupported WorkloadSource type: {source.GetType().Name}"),
        };
    }

    // ---- PROJECT CONTEXT PERSISTENCE ----

    private async Task AppendRunSummaryToProjectContextAsync(Job job, JobRun run, CancellationToken ct)
    {
        var projectId = PlaceContext.Domain.ValueObjects.ProjectId.From(run.ProjectId);
        var context = await _contexts.GetForProjectAsync(projectId, ct)
            ?? ProjectContext.Start(projectId, _clock.UtcNow);

        var sb = new StringBuilder();
        sb.AppendLine($"## Job run: {job.Name} [{run.Status}] — {run.FinishedAt:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"Run ID: `{run.Id:N}`  |  Job: `{job.Id:N}`  |  Shards: {run.ShardResults.Count}");
        sb.AppendLine();

        foreach (var shard in run.ShardResults.OrderBy(s => s.Index))
        {
            sb.AppendLine($"### Shard {shard.Index} [{shard.Outcome}] (exit {shard.ExitCode})");
            if (shard.Artifact is { } art)
            {
                var truncated = art.Length > 2000 ? art[..2000] + "\n… (truncated)" : art;
                sb.AppendLine("```json");
                sb.AppendLine(truncated);
                sb.AppendLine("```");
            }
            else
            {
                sb.AppendLine("_(no artifact)_");
            }
        }

        if (run.ReduceResult is { } reduce)
        {
            sb.AppendLine($"### Reduce [{(reduce.Succeeded ? "succeeded" : "failed")}] (exit {reduce.ExitCode})");
            if (reduce.Artifact is { } rart)
            {
                var truncated = rart.Length > 4000 ? rart[..4000] + "\n… (truncated)" : rart;
                sb.AppendLine("```json");
                sb.AppendLine(truncated);
                sb.AppendLine("```");
            }
        }

        context.Append(sb.ToString().TrimEnd(), _clock.UtcNow);
        await _contexts.SaveAsync(context, ct);
    }

    private static string? CombineLog(string stdout, string stderr)
    {
        if (string.IsNullOrWhiteSpace(stdout) && string.IsNullOrWhiteSpace(stderr))
            return null;
        if (string.IsNullOrWhiteSpace(stderr)) return stdout;
        if (string.IsNullOrWhiteSpace(stdout)) return stderr;
        return stdout + "\n--- stderr ---\n" + stderr;
    }
}
