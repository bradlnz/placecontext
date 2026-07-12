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
    private readonly IEmbeddingGateway? _embeddings;
    private readonly IRunEmbeddingRepository? _embeddingStore;
    private readonly IProjectSecretRepository? _secretRepo;
    private readonly ISecretProtector? _secretProtector;
    private readonly PostJobActionService? _postActions;
    private readonly JobRunDataRecorder? _runData;
    private readonly DataMappingIngestionService? _dataMappings;
    private readonly EntityTagService? _entityTags;
    private IReadOnlyDictionary<string, string> _runSecrets = new Dictionary<string, string>();

    public RunJobHandler(
        IJobRepository jobs,
        IJobRunRepository runs,
        IProjectContextRepository contexts,
        IWorkloadRunner runner,
        IUnitOfWork uow,
        IClock clock,
        // Optional so unit tests can construct the handler without the event/embedding/secret layers; DI always supplies them.
        EventDispatchService? events = null,
        IEmbeddingGateway? embeddings = null,
        IRunEmbeddingRepository? embeddingStore = null,
        IProjectSecretRepository? secretRepo = null,
        ISecretProtector? secretProtector = null,
        PostJobActionService? postActions = null,
        JobRunDataRecorder? runData = null,
        DataMappingIngestionService? dataMappings = null,
        EntityTagService? entityTags = null)
    {
        _secretRepo = secretRepo;
        _secretProtector = secretProtector;
        _postActions = postActions;
        _runData = runData;
        _dataMappings = dataMappings;
        _entityTags = entityTags;
        _jobs = jobs;
        _runs = runs;
        _contexts = contexts;
        _runner = runner;
        _uow = uow;
        _clock = clock;
        _events = events;
        _embeddings = embeddings;
        _embeddingStore = embeddingStore;
    }

    public async Task<JobRunDetailView> HandleAsync(RunJobCommand command, CancellationToken ct = default)
    {
        var job = await _jobs.GetByIdAsync(command.JobId, ct)
            ?? throw new InvalidOperationException($"Job {command.JobId} not found.");

        // Load the project's vault secrets (decrypted) for injection as env into each sandbox. Never
        // persisted to the run snapshot — only merged into the live WorkloadRunRequest below.
        _runSecrets = await LoadSecretsAsync(job.ProjectId, ct);

        // An input override (modal-collected parameters or event-injected fields) replaces the stored
        // shard payloads with a single shard carrying the supplied payload.
        var effectiveMap = command.InputPayload is { } p
            ? new MapSpec(job.MapSpec.Source, new[] { p }, job.MapSpec.Env)
            : job.MapSpec;

        // Snapshot the effective spec onto the run at start-time (captures AllowNetworkEgress too).
        var snapshot = WorkloadSnapshot.From(effectiveMap, job.ReduceSpec, job.ConcurrencyLimit,
            job.AllowNetworkEgress);
        var startedAt = _clock.UtcNow;
        var run = JobRun.Start(job.Id, job.ProjectId, startedAt, snapshot, command.RunId);
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
        // Commit the terminal status FIRST and on its own, so the run can never linger in "Running"
        // (and artifacts can never be blocked) behind the slow, best-effort enrichment below — local
        // CPU LLM organize/embed can take minutes.
        var finishedAt = _clock.UtcNow;
        run.Complete(shardResults, reduceResult, finishedAt);
        await _runs.UpdateAsync(run, ct);
        await _uow.SaveChangesAsync(ct);

        // Post-job actions: turn this run's artifacts into stored outputs (HTML report / chart / CSV /
        // raw bundle) surfaced as links. Done before the slow LLM enrichment so links appear promptly.
        // Best-effort — never fails the run.
        if (_postActions is not null)
        {
            try { await _postActions.RunAsync(job, run, ct); }
            catch { /* isolated inside the service too; belt-and-suspenders */ }
        }

        // The run's results also land in the project's own database: appended to the read-only
        // job_run_data table so they can be queried and charted from the Data tab. Best-effort.
        if (_runData is not null)
            await _runData.RecordAsync(job, run, ct);

        // The project's data map: mappings declared for this job extract records from the run's
        // primary artifact and append them to their target tables. Best-effort.
        if (_dataMappings is not null)
            await _dataMappings.IngestAsync(job, run, ct);

        // The relation tree: values in this run's output that match entity keys (a site's address,
        // say) persist run ⇄ entity tags, linking the job and its artifacts to those records.
        if (_entityTags is not null)
            await _entityTags.TagRunAsync(job, run, ct);

        // Persist a summary of the run into the project's context document and embed it for
        // search/the dependency graph. Best-effort — isolated so it can't fail or stall the run.
        try
        {
            await AppendRunSummaryToProjectContextAsync(job, run, ct);
            await _uow.SaveChangesAsync(ct);
        }
        catch { /* organize/embed/context-append is best-effort enrichment */ }

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
            var request = BuildRequest(job.MapSpec.Source, payload, MergeSecrets(job.MapSpec.Env),
                Array.Empty<(string, string)>(), correlationId, job.AllowNetworkEgress, job.TimeoutSeconds);

            var result = await _runner.RunAsync(request, ct);
            var outcome = job.ExitCodePolicy.Classify(result.ExitCode);
            var log = CombineLog(result.Stdout, result.Stderr);

            return new ShardResult(index, result.ExitCode, outcome, result.Artifact, log,
                CollectArtifacts(result.Artifact, result.Artifacts));
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
        var request = BuildRequest(reduceSpec.Source, "{}", MergeSecrets(reduceSpec.Env),
            artifactMounts, correlationId, job.AllowNetworkEgress, job.TimeoutSeconds);

        var result = await _runner.RunAsync(request, ct);
        var succeeded = job.ExitCodePolicy.SuccessCodes.Contains(result.ExitCode);
        var log = CombineLog(result.Stdout, result.Stderr);

        return new ReduceResult(result.ExitCode, succeeded, result.Artifact, log,
            CollectArtifacts(result.Artifact, result.Artifacts));
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
        bool allowNetworkEgress,
        int timeoutSeconds)
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
                AllowNetworkEgress: allowNetworkEgress,
                TimeoutSeconds: timeoutSeconds),

            WorkloadSource.CodeWorkload code => new WorkloadRunRequest(
                Image: null,
                StdinPayload: stdinPayload,
                Env: env,
                ArtifactMounts: artifactMounts,
                CorrelationId: correlationId,
                CodeFiles: code.Files.Select(f => (f.Path, f.Content)).ToList(),
                RuntimeId: code.RuntimeId,
                Entrypoint: code.Entrypoint,
                AllowNetworkEgress: allowNetworkEgress,
                TimeoutSeconds: timeoutSeconds),

            _ => throw new InvalidOperationException($"Unsupported WorkloadSource type: {source.GetType().Name}"),
        };
    }

    // Decrypt the project's vault secrets for run-time env injection. Empty when the vault isn't wired.
    private async Task<IReadOnlyDictionary<string, string>> LoadSecretsAsync(Guid projectId, CancellationToken ct)
    {
        if (_secretRepo is null || _secretProtector is null) return new Dictionary<string, string>();
        var ciphers = await _secretRepo.GetCiphersAsync(projectId, ct);
        var plain = new Dictionary<string, string>(ciphers.Count);
        foreach (var (name, cipher) in ciphers)
        {
            try { plain[name] = _secretProtector.Unprotect(cipher); } catch { /* skip un-decryptable */ }
        }
        return plain;
    }

    // Vault secrets form the base env; the job's own env overrides on key collision.
    private IReadOnlyDictionary<string, string> MergeSecrets(IReadOnlyDictionary<string, string> env)
    {
        if (_runSecrets.Count == 0) return env;
        var merged = new Dictionary<string, string>(_runSecrets);
        foreach (var (k, v) in env) merged[k] = v;
        return merged;
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

        var toStore = sb.ToString().TrimEnd();

        context.Append(toStore, _clock.UtcNow);
        await _contexts.SaveAsync(context, ct);

        // Vectorize the organized output (Voyage) and store it so runs become semantically searchable
        // and linkable in the dependency graph. Best-effort — never fails the run.
        if (_embeddings is { IsEnabled: true } && _embeddingStore is not null)
        {
            try
            {
                var text = toStore.Length > 8000 ? toStore[..8000] : toStore;
                var vectors = await _embeddings.EmbedAsync(new[] { text }, ct);
                if (vectors.Count > 0 && vectors[0].Length > 0)
                {
                    var embedding = RunEmbedding.Create(run.Id, job.Id, run.ProjectId, text, vectors[0], _clock.UtcNow);
                    await _embeddingStore.AddAsync(embedding, ct);
                }
            }
            catch
            {
                // Embedding is best-effort; a gateway/store failure must not fail the job run.
            }
        }
    }

    private static IReadOnlyList<RunArtifact> MapArtifacts(IReadOnlyList<WorkloadArtifact>? artifacts)
        => artifacts is null or { Count: 0 }
            ? Array.Empty<RunArtifact>()
            : artifacts.Select(a => new RunArtifact(a.Name, a.Content, a.IsBinary)).ToList();

    /// <summary>
    /// A step's named artifacts: the files captured from /out, plus any artifacts the job embedded
    /// in its returned JSON (an "artifacts" array with filename + base64/content — the only channel
    /// image workloads have in-cluster). A real /out file wins a name collision.
    /// </summary>
    private static IReadOnlyList<RunArtifact> CollectArtifacts(string? primaryArtifact, IReadOnlyList<WorkloadArtifact>? files)
    {
        var named = MapArtifacts(files);
        var embedded = EmbeddedArtifacts.Extract(primaryArtifact);
        if (embedded.Count == 0) return named;

        var seen = named.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return named.Concat(embedded.Where(e => seen.Add(e.Name))).ToList();
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
