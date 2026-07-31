using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Observability;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Executes a job: snapshots the current spec onto the run, fans out all map shards (bounded
/// concurrency), optionally runs the reduce step with the shard artifacts mounted as inputs,
/// aggregates status via the job's ExitCodePolicy, and persists the run.
/// A summary of the completed run is embedded for semantic search over run outputs.
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
    private readonly IWorkloadRunner _runner;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly EventDispatchService? _events;
    private readonly IEmbeddingGateway? _embeddings;
    private readonly IRunEmbeddingRepository? _embeddingStore;
    private readonly IContentIndexer? _contentIndexer;
    private readonly IProjectSecretRepository? _secretRepo;
    private readonly ISecretProtector? _secretProtector;
    private readonly PostJobActionService? _postActions;
    private readonly JobRunDataRecorder? _runData;
    private readonly DataMappingIngestionService? _dataMappings;
    private readonly EntityTagService? _entityTags;
    private readonly IMcpConnectionRepository? _mcpRepo;
    private readonly IDataEncryptor? _encryptor;
    private readonly IObjectStore? _objectStore;
    private IReadOnlyDictionary<string, string> _runSecrets = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string> _mcpEnv = new Dictionary<string, string>();

    public RunJobHandler(
        IJobRepository jobs,
        IJobRunRepository runs,
        IWorkloadRunner runner,
        IUnitOfWork uow,
        IClock clock,
        // Optional so unit tests can construct the handler without the event/embedding/secret layers; DI always supplies them.
        EventDispatchService? events = null,
        IEmbeddingGateway? embeddings = null,
        IRunEmbeddingRepository? embeddingStore = null,
        IContentIndexer? contentIndexer = null,
        IProjectSecretRepository? secretRepo = null,
        ISecretProtector? secretProtector = null,
        PostJobActionService? postActions = null,
        JobRunDataRecorder? runData = null,
        DataMappingIngestionService? dataMappings = null,
        EntityTagService? entityTags = null,
        IMcpConnectionRepository? mcpRepo = null,
        IDataEncryptor? encryptor = null,
        IObjectStore? objectStore = null)
    {
        _secretRepo = secretRepo;
        _secretProtector = secretProtector;
        _postActions = postActions;
        _runData = runData;
        _dataMappings = dataMappings;
        _entityTags = entityTags;
        _mcpRepo = mcpRepo;
        _encryptor = encryptor;
        _objectStore = objectStore;
        _jobs = jobs;
        _runs = runs;
        _runner = runner;
        _uow = uow;
        _clock = clock;
        _events = events;
        _embeddings = embeddings;
        _embeddingStore = embeddingStore;
        _contentIndexer = contentIndexer;
    }

    public async Task<JobRunDetailView> HandleAsync(RunJobCommand command, CancellationToken ct = default)
    {
        var job = await _jobs.GetByIdAsync(command.JobId, ct)
            ?? throw new InvalidOperationException($"Job {command.JobId} not found.");

        // OpenTelemetry: one span (and a started/completed metric pair) per run — the realtime,
        // exportable view into the jobs pipeline. Inert unless the Host wired an exporter.
        using var runSpan = JobTelemetry.Activity.StartActivity("job.run", ActivityKind.Internal);
        runSpan?.SetTag("job.id", job.Id);
        runSpan?.SetTag("job.name", job.Name);
        runSpan?.SetTag("project.id", job.ProjectId);
        runSpan?.SetTag("job.replay", command.ReplayOfRunId is not null);
        JobTelemetry.RunsStarted.Add(1,
            new("job.id", job.Id.ToString()), new("project.id", job.ProjectId.ToString()),
            new("replay", command.ReplayOfRunId is not null));

        // Load the project's vault secrets (decrypted) for injection as env into each sandbox. Never
        // persisted to the run snapshot — only merged into the live WorkloadRunRequest below.
        _runSecrets = await LoadSecretsAsync(job.ProjectId, ct);

        // Load MCP connection tokens for the job's enabled connections. Injected as env var.
        _mcpEnv = await LoadMcpEnvAsync(job.McpConnectionIds, ct);

        // Resolve the execution plan: replaying a prior run reproduces its captured snapshot verbatim,
        // otherwise we run the job's current spec (optionally with a single-shard input override).
        MapSpec effectiveMap;
        ReduceSpec? effectiveReduce;
        int concurrency;
        bool allowEgress;
        if (command.ReplayOfRunId is { } replayId)
        {
            var prior = await _runs.GetByIdAsync(replayId, ct)
                ?? throw new InvalidOperationException($"Run {replayId} not found — cannot replay.");
            var s = prior.Snapshot;
            effectiveMap = new MapSpec(s.MapSource, s.InputPayloads, s.MapEnv);
            effectiveReduce = s.ReduceSource is not null
                ? new ReduceSpec(s.ReduceSource, s.ReduceEnv ?? new Dictionary<string, string>())
                : null;
            concurrency = s.ConcurrencyLimit;
            allowEgress = s.AllowNetworkEgress;
        }
        else
        {
            // An input override (modal-collected parameters or event-injected fields) replaces the
            // stored shard payloads with a single shard carrying the supplied payload.
            effectiveMap = command.InputPayload is { } p
                ? new MapSpec(job.MapSpec.Source, new[] { p }, job.MapSpec.Env)
                : job.MapSpec;
            effectiveReduce = job.ReduceSpec;
            concurrency = job.ConcurrencyLimit;
            allowEgress = job.AllowNetworkEgress;
        }

        // Snapshot the effective spec onto the run at start-time (captures AllowNetworkEgress too).
        var snapshot = WorkloadSnapshot.From(effectiveMap, effectiveReduce, concurrency, allowEgress);
        var startedAt = _clock.UtcNow;
        var run = JobRun.Start(job.Id, job.ProjectId, startedAt, snapshot, command.RunId,
            command.AttemptNumber, command.OriginalRunId);
        runSpan?.SetTag("run.id", run.Id);
        runSpan?.SetTag("run.attempt", command.AttemptNumber);
        await _runs.AddAsync(run, ct);
        await _uow.SaveChangesAsync(ct);

        // ---- MAP PHASE: fan-out shards with bounded concurrency ----
        var shardResults = await RunMapShardsAsync(job, effectiveMap, concurrency, allowEgress, run.Id, ct);

        // ---- REDUCE PHASE (optional) ----
        ReduceResult? reduceResult = null;
        if (effectiveReduce is { } reduceSpec)
        {
            reduceResult = await RunReduceStepAsync(job, run.Id, shardResults, reduceSpec, allowEgress, ct);
        }

        // ---- AGGREGATE + PERSIST ----
        // Commit the terminal status FIRST and on its own, so the run can never linger in "Running"
        // (and artifacts can never be blocked) behind the slow, best-effort enrichment below — local
        // CPU LLM organize/embed can take minutes.
        var finishedAt = _clock.UtcNow;
        run.Complete(shardResults, reduceResult, finishedAt);
        await _runs.UpdateAsync(run, ct);
        await _uow.SaveChangesAsync(ct);

        // Record the terminal status + wall-clock as OTel metrics and span tags.
        var status = run.Status.ToString();
        runSpan?.SetTag("run.status", status);
        runSpan?.SetTag("run.shards", run.ShardResults.Count);
        runSpan?.SetStatus(run.Status == Domain.Entities.JobRunStatus.Failed ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        JobTelemetry.RunsCompleted.Add(1, new("status", status), new("job.id", job.Id.ToString()));
        JobTelemetry.RunDuration.Record((finishedAt - startedAt).TotalMilliseconds,
            new("status", status), new("job.id", job.Id.ToString()));

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

        // Embed a summary of the run for search/the dependency graph. Best-effort — isolated so it
        // can't fail or stall the run.
        try
        {
            await EmbedRunSummaryAsync(job, run, ct);
            await _uow.SaveChangesAsync(ct);
        }
        catch { /* organize/embed is best-effort enrichment */ }

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
        Job job, MapSpec map, int concurrency, bool allowEgress, Guid runId, CancellationToken ct)
    {
        var payloads = map.InputPayloads;
        var semaphore = new SemaphoreSlim(concurrency, concurrency);
        var tasks = new Task<ShardResult>[payloads.Count];

        for (var i = 0; i < payloads.Count; i++)
        {
            var index = i;
            var payload = payloads[i];
            tasks[index] = RunShardAsync(job, map, allowEgress, runId, index, payload, semaphore, ct);
        }

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<ShardResult> RunShardAsync(
        Job job, MapSpec map, bool allowEgress, Guid runId, int index, string payload,
        SemaphoreSlim semaphore, CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        using var shardSpan = JobTelemetry.Activity.StartActivity("job.shard", ActivityKind.Internal);
        shardSpan?.SetTag("run.id", runId);
        shardSpan?.SetTag("shard.index", index);
        var sw = Stopwatch.StartNew();
        try
        {
            var correlationId = $"{runId:N}-map-{index}";
            // Source/env/egress come from the effective plan (job spec or a replayed snapshot); the
            // exit-code policy and timeout are job metadata not captured in the snapshot.
            var resolvedPayload = await ResolveFileInputsAsync(payload, job.ProjectId, job.TimeoutSeconds, ct);
            var request = BuildRequest(map.Source, resolvedPayload, MergeMcpEnv(MergeSecrets(map.Env)),
                Array.Empty<(string, string)>(), correlationId, allowEgress, job.TimeoutSeconds);

            var result = await _runner.RunAsync(request, ct);
            var outcome = job.ExitCodePolicy.Classify(result.ExitCode);
            var log = CombineLog(result.Stdout, result.Stderr);

            sw.Stop();
            var outcomeLabel = outcome.ToString();
            shardSpan?.SetTag("shard.outcome", outcomeLabel);
            shardSpan?.SetTag("shard.exit_code", result.ExitCode);
            var shardTags = new TagList { { "outcome", outcomeLabel } };
            JobTelemetry.ShardDuration.Record(sw.Elapsed.TotalMilliseconds, shardTags);
            JobTelemetry.ShardsCompleted.Add(1, shardTags);

            return new ShardResult(index, result.ExitCode, outcome, result.Artifact, log,
                CollectArtifacts(result.Artifact, result.Artifacts));
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Turns durable private-object markers into short-lived download links immediately before a
    /// shard starts. The run snapshot keeps only bucket/key metadata, so retries and replays get a
    /// fresh URL and no signed credential is persisted.
    /// </summary>
    private async Task<string> ResolveFileInputsAsync(
        string payload, Guid projectId, int timeoutSeconds, CancellationToken ct)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(payload); }
        catch (JsonException) { return payload; }
        if (root is null) return payload;

        var changed = await ResolveFileInputsNodeAsync(root, projectId, timeoutSeconds, ct);
        return changed ? root.ToJsonString() : payload;
    }

    private async Task<bool> ResolveFileInputsNodeAsync(
        JsonNode node, Guid projectId, int timeoutSeconds, CancellationToken ct)
    {
        if (node is JsonObject obj
            && obj.TryGetPropertyValue("$file", out var marker)
            && marker is JsonObject file)
        {
            file.Remove("download_url");
            file.Remove("download_error");
            file.Remove("resolved_by_devcontext");

            try
            {
                if (_objectStore is null || !_objectStore.IsEnabled)
                    return SetFileError(file, "File storage is not configured.");

                var bucket = file["bucket"]?.GetValue<string>();
                var key = file["key"]?.GetValue<string>();
                var allowedPrefix = $"job-inputs/{projectId:N}/";
                if (!string.Equals(bucket, _objectStore.ReportsBucket, StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(key)
                    || !key.StartsWith(allowedPrefix, StringComparison.Ordinal))
                    return SetFileError(file, "The uploaded file reference is invalid for this project.");

                if (!await _objectStore.ExistsAsync(bucket!, key!, ct))
                    return SetFileError(file, "The uploaded file is no longer available.");

                var ttl = TimeSpan.FromSeconds(Math.Max(3600, timeoutSeconds + 900));
                file["download_url"] = await _objectStore.PresignDownloadAsync(bucket!, key!, ttl, ct);
                file["resolved_by_devcontext"] = true;
                return true;
            }
            catch
            {
                return SetFileError(file, "Could not create a secure download link.");
            }
        }

        var changed = false;
        if (node is JsonObject container)
        {
            foreach (var child in container.Select(pair => pair.Value).Where(value => value is not null).ToList())
                changed |= await ResolveFileInputsNodeAsync(child!, projectId, timeoutSeconds, ct);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array.Where(value => value is not null).ToList())
                changed |= await ResolveFileInputsNodeAsync(child!, projectId, timeoutSeconds, ct);
        }
        return changed;
    }

    private static bool SetFileError(JsonObject file, string message)
    {
        file["download_error"] = message;
        return true;
    }

    // ---- REDUCE PHASE ----

    private async Task<ReduceResult> RunReduceStepAsync(
        Job job, Guid runId,
        List<ShardResult> shardResults,
        ReduceSpec reduceSpec,
        bool allowEgress,
        CancellationToken ct)
    {
        // Build artifact mounts: each shard's artifact content is passed to the runner which
        // materialises them as temp files and mounts them read-only at /in/{index}/result.json.
        var artifactMounts = shardResults
            .Where(s => s.Artifact is not null)
            .Select(s => (s.Artifact!, $"/in/{s.Index}/result.json"))
            .ToList();

        var correlationId = $"{runId:N}-reduce";
        var request = BuildRequest(reduceSpec.Source, "{}", MergeMcpEnv(MergeSecrets(reduceSpec.Env)),
            artifactMounts, correlationId, allowEgress, job.TimeoutSeconds);

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

    // MCP connection tokens are injected on top of the merged env.
    private IReadOnlyDictionary<string, string> MergeMcpEnv(IReadOnlyDictionary<string, string> env)
    {
        if (_mcpEnv.Count == 0) return env;
        var merged = new Dictionary<string, string>(env);
        foreach (var (k, v) in _mcpEnv) merged[k] = v;
        return merged;
    }

    // Load MCP connection tokens for the job's enabled connections. Returns empty if none.
    private async Task<IReadOnlyDictionary<string, string>> LoadMcpEnvAsync(IReadOnlyList<Guid> mcpConnectionIds, CancellationToken ct)
    {
        if (_mcpRepo is null || _encryptor is null || mcpConnectionIds.Count == 0)
            return new Dictionary<string, string>();

        var env = new Dictionary<string, string>();
        var connections = new List<(string Name, string Url, string Token)>();
        var purpose = "mcp.oauth.tokens";

        foreach (var connId in mcpConnectionIds)
        {
            var conn = await _mcpRepo.GetByIdAsync(connId, ct);
            if (conn is null) continue;

            var token = conn.AuthType == "oauth"
                ? (_encryptor.Unprotect(conn.OAuthAccessToken, purpose) ?? "")
                : conn.AuthToken ?? "";

            connections.Add((conn.Name, conn.EndpointUrl ?? "", token));
        }

        if (connections.Count > 0)
        {
            env["MCP_CONNECTIONS_JSON"] = JsonSerializer.Serialize(
                connections.Select(c => new { c.Name, Url = c.Url, Token = c.Token }));
        }

        return env;
    }

    // ---- RUN SUMMARY EMBEDDING ----

    private async Task EmbedRunSummaryAsync(Job job, JobRun run, CancellationToken ct)
    {
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

        // Vectorize the organized output for RAG + dependency graph. Dual-write: legacy
        // job_run_embeddings (encrypted text) and universal content_embeddings. Best-effort.
        var text = toStore.Length > 8000 ? toStore[..8000] : toStore;
        if (_embeddings is { IsEnabled: true } && _embeddingStore is not null)
        {
            try
            {
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
        if (_contentIndexer is { IsEnabled: true })
        {
            try
            {
                await _contentIndexer.IndexAsync(run.ProjectId, ContentKind.RunOutput, $"run:{run.Id}", text, ct);
            }
            catch { /* best-effort */ }
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
