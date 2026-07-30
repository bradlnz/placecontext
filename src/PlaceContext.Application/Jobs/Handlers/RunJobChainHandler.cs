using System.Diagnostics;
using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Observability;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Runs a chain as a staged pipeline of <see cref="ChainStage"/>s. A stage with a single job runs as
/// an ordinary sequential step, exactly as before; a stage with more than one job is a parallel
/// fan-out group — every branch is dispatched concurrently (bounded — see
/// <see cref="MaxConcurrentBranches"/>) and the stage is all-or-nothing: it only "succeeds" once
/// every branch reaches a non-Failed terminal state. If any branch in a stage ends Failed, the whole
/// chain is marked Failed and every later stage — including the join that would have followed a
/// fan-out group — is left Pending, which <see cref="ChainRun.Complete"/> turns into Skipped; the
/// join never runs. A Partial branch downgrades the chain to Partial but does not by itself halt it.
///
/// Each stage's primary output becomes the next stage's input payload, threaded exactly as a linear
/// chain always has: a single-job stage passes its one job's primary output through unchanged; a
/// fan-out stage combines every branch's primary output into one JSON array (the same "raw when
/// valid JSON, else JSON-encoded" convention already used to combine multiple map-shard artifacts)
/// so the join stage's job receives all of its upstream branches' outputs as its stdin input.
///
/// The run is persisted the moment it starts and saved on every step transition (pending → running →
/// outcome) — including concurrent transitions within a fan-out stage, serialized through a lock so
/// the portal always observes a consistent snapshot — so the portal can watch the pipeline progress
/// live and keep a history. A <c>job.chain</c> OTel span wraps the whole run (see
/// <see cref="PlaceContext.Application.Observability.JobTelemetry"/>); because each dispatched
/// <c>RunJobCommand</c> runs in the same async flow that started the span, its own <c>job.run</c>
/// span nests under it automatically — including for concurrent branches, since each awaited Task's
/// captured <see cref="Activity.Current"/> is independent of its siblings once forked.
/// </summary>
public sealed class RunJobChainHandler : ICommandHandler<RunJobChainCommand, ChainRunView>
{
    /// <summary>Caps how many branches of a fan-out stage dispatch at once — conservative, since each
    /// dispatched <c>RunJobCommand</c> opens its own DI scope/DbContext and may itself fan out map
    /// shards under its own concurrency limit.</summary>
    private const int MaxConcurrentBranches = 4;

    private readonly IJobChainRepository _chains;
    private readonly IJobRepository _jobs;
    private readonly IChainRunRepository _runs;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly IJobRunner _jobRunner;

    public RunJobChainHandler(IJobChainRepository chains, IJobRepository jobs, IChainRunRepository runs,
        IUnitOfWork uow, IClock clock, IJobRunner jobRunner,
        // Optional so unit tests construct the handler unchanged; DI always supplies it.
        DataMappingIngestionService? dataMappings = null)
    {
        _dataMappings = dataMappings;
        _chains = chains;
        _jobs = jobs;
        _runs = runs;
        _uow = uow;
        _clock = clock;
        _jobRunner = jobRunner;
    }

    private readonly DataMappingIngestionService? _dataMappings;

    public async Task<ChainRunView> HandleAsync(RunJobChainCommand command, CancellationToken ct = default)
    {
        var chain = await _chains.GetByIdAsync(command.ChainId, ct)
            ?? throw new InvalidOperationException($"Chain {command.ChainId} not found.");

        // Snapshot every step's job up front — names go on the run so its history stands alone.
        var stepJobs = new List<Job?>(chain.StepJobIds.Count);
        foreach (var jobId in chain.StepJobIds)
            stepJobs.Add(await _jobs.GetByIdAsync(jobId, ct));
        var names = stepJobs.Select((j, i) => j?.Name ?? "(deleted)").ToList();

        using var chainSpan = JobTelemetry.Activity.StartActivity("job.chain", ActivityKind.Internal);
        chainSpan?.SetTag("chain.id", chain.Id);
        chainSpan?.SetTag("chain.name", chain.Name);
        chainSpan?.SetTag("project.id", chain.ProjectId);
        JobTelemetry.ChainsStarted.Add(1, new("chain.id", chain.Id.ToString()), new("project.id", chain.ProjectId.ToString()));
        var chainStartedAt = _clock.UtcNow;

        var chainRun = ChainRun.Start(chain, names, chainStartedAt, command.ChainRunId);
        chainSpan?.SetTag("chain.run.id", chainRun.Id);
        await _runs.AddAsync(chainRun, ct);
        await _uow.SaveChangesAsync(ct);

        // Serializes every write onto chainRun/_runs/_uow — dispatching itself (_dispatcher.Send) is
        // NOT held under this lock, so a fan-out stage's branches genuinely run concurrently; only the
        // "record the transition" moments are serialized, keeping every persisted snapshot consistent.
        using var progressLock = new SemaphoreSlim(1, 1);
        using var dispatchGate = new SemaphoreSlim(MaxConcurrentBranches, MaxConcurrentBranches);

        var status = ChainRunStatus.Succeeded;
        string? payload = command.InputPayload; // first stage: caller's payload, or the job's stored shards
        var flatIndex = 0;

        for (var stageIndex = 0; stageIndex < chain.Stages.Count; stageIndex++)
        {
            var stage = chain.Stages[stageIndex];

            // ── Evaluate gate before the stage ────────────────────────────────────────────────
            if (stage.Gate is { } gate)
            {
                var gateResult = gate.Evaluate(payload);
                if (gateResult.WaitDuration is { } wait)
                {
                    await Task.Delay(wait, ct);
                }
                if (!gateResult.Proceed)
                {
                    // Condition gate false: skip this stage, keep payload unchanged.
                    for (var b = 0; b < stage.JobIds.Count; b++)
                    {
                        var idx = flatIndex + b;
                        chainRun.MarkStepFinished(idx, runId: null, ChainStepStatus.Skipped, _clock.UtcNow);
                    }
                    flatIndex += stage.JobIds.Count;
                    await SaveProgressAsync(chainRun, ct);
                    continue;
                }
            }

            var stageStartFlat = flatIndex;
            var stagePayload = payload; // every branch of this stage reads the same upstream payload
            var branchOutputs = new string?[stage.JobIds.Count];
            var branchStatuses = new ChainStepStatus[stage.JobIds.Count];

            var branchTasks = Enumerable.Range(0, stage.JobIds.Count).Select(async branchIndex =>
            {
                var flat = stageStartFlat + branchIndex;
                var job = stepJobs[flat];

                if (job is null)
                {
                    branchStatuses[branchIndex] = ChainStepStatus.Failed;
                    await WithProgressLockAsync(progressLock, async () =>
                    {
                        chainRun.MarkStepFinished(flat, runId: null, ChainStepStatus.Failed, _clock.UtcNow);
                        await SaveProgressAsync(chainRun, ct);
                    });
                    return;
                }

                // Pre-allocate the branch's run id and record it before dispatching, so the live
                // pipeline (and the run-status watcher) can address it while it is still executing.
                var stepRunId = Guid.NewGuid();
                await WithProgressLockAsync(progressLock, async () =>
                {
                    chainRun.MarkStepRunning(flat, stepRunId, _clock.UtcNow);
                    await SaveProgressAsync(chainRun, ct);
                });

                var stepPayload = command.StepPayloadOverrides is { } overrides && overrides.TryGetValue(flat, out var args)
                    ? MergePayload(stagePayload, args)
                    : stagePayload;

                JobRunDetailView run;
                await dispatchGate.WaitAsync(ct);
                try
                {
                    run = await _jobRunner.RunAsync(stage.JobIds[branchIndex], stepPayload, stepRunId, ct: ct);
                }
                finally
                {
                    dispatchGate.Release();
                }

                var outcome = ParseStepOutcome(run.Status);
                branchStatuses[branchIndex] = outcome;
                branchOutputs[branchIndex] = PrimaryOutput(run);

                await WithProgressLockAsync(progressLock, async () =>
                {
                    chainRun.MarkStepFinished(flat, run.Id, outcome, _clock.UtcNow);
                    await SaveProgressAsync(chainRun, ct);
                });
            });

            await Task.WhenAll(branchTasks);
            flatIndex += stage.JobIds.Count;

            if (Array.IndexOf(branchStatuses, ChainStepStatus.Failed) >= 0)
            {
                // All-or-nothing: any branch failing halts the chain. Every later stage — including
                // the join that would have followed a fan-out group — stays Pending here and is
                // turned into Skipped by ChainRun.Complete below; it never runs.
                status = ChainRunStatus.Failed;
                break;
            }
            if (Array.IndexOf(branchStatuses, ChainStepStatus.Cancelled) >= 0)
            {
                // A cancelled branch also halts the chain, but marks it Cancelled not Failed.
                status = ChainRunStatus.Cancelled;
                break;
            }
            if (Array.IndexOf(branchStatuses, ChainStepStatus.Partial) >= 0)
                status = ChainRunStatus.Partial;

            payload = stage.JobIds.Count == 1 ? branchOutputs[0] : CombineOutputs(branchOutputs);
        }

        chainRun.Complete(status, payload, _clock.UtcNow);
        await SaveProgressAsync(chainRun, ct);

        var finishedAt = _clock.UtcNow;
        chainSpan?.SetTag("status", status.ToString());
        chainSpan?.SetTag("chain.steps.json", BuildStepsTelemetryJson(chainRun));
        JobTelemetry.ChainsCompleted.Add(1, new("status", status.ToString()), new("chain.id", chain.Id.ToString()));
        JobTelemetry.ChainDuration.Record((finishedAt - chainStartedAt).TotalMilliseconds,
            new("status", status.ToString()), new("chain.id", chain.Id.ToString()));

        // The data map's chain edges: the pipeline's final output flows into its mapped tables.
        // Best-effort — ingestion must never fail the chain.
        if (_dataMappings is not null)
        {
            try { await _dataMappings.IngestChainOutputAsync(chain.Id, chainRun.Id, chain.ProjectId, payload, ct); }
            catch { /* isolated inside the service too */ }
        }

        return ChainRunMapper.ToView(chainRun);
    }

    private static async Task WithProgressLockAsync(SemaphoreSlim gate, Func<Task> action)
    {
        await gate.WaitAsync();
        try { await action(); }
        finally { gate.Release(); }
    }

    // Fold collected step parameters over the chained input: two JSON objects merge shallowly
    // (parameter values win); anything else keeps the chained input under "previous" beside them.
    private static string MergePayload(string? chained, string args)
    {
        if (string.IsNullOrWhiteSpace(chained)) return args;
        try
        {
            var argsNode = System.Text.Json.Nodes.JsonNode.Parse(args) as System.Text.Json.Nodes.JsonObject
                ?? throw new JsonException();
            if (System.Text.Json.Nodes.JsonNode.Parse(chained) is System.Text.Json.Nodes.JsonObject prevObj)
            {
                foreach (var (k, v) in argsNode.ToList())
                    prevObj[k] = v?.DeepClone();
                return prevObj.ToJsonString();
            }
            argsNode["previous"] = System.Text.Json.Nodes.JsonNode.Parse(chained);
            return argsNode.ToJsonString();
        }
        catch
        {
            return args; // unparseable input — the collected parameters win
        }
    }

    private async Task SaveProgressAsync(ChainRun run, CancellationToken ct)
    {
        await _runs.UpdateAsync(run, ct);
        await _uow.SaveChangesAsync(ct);
    }

    private static ChainStepStatus ParseStepOutcome(string runStatus) => runStatus switch
    {
        "Succeeded" => ChainStepStatus.Succeeded,
        "Partial" => ChainStepStatus.Partial,
        "Cancelled" => ChainStepStatus.Cancelled,
        _ => ChainStepStatus.Failed,
    };

    /// <summary>The run's primary output, as the next stage's stdin payload: the reduce artifact when
    /// present (the final aggregate), a lone shard's artifact as-is, else a JSON array of the shard
    /// artifacts (raw values when they are JSON, JSON-encoded strings otherwise). When no stdout
    /// artifact was produced, non-binary named output files (<see cref="RunArtifactView"/>) are
    /// used as a fallback so downstream chain stages still receive the run's data.</summary>
    internal static string? PrimaryOutput(JobRunDetailView run)
    {
        if (run.ReduceResult?.Artifact is { Length: > 0 } reduce) return reduce;

        var artifacts = run.ShardResults
            .OrderBy(s => s.Index)
            .Where(s => !string.IsNullOrWhiteSpace(s.Artifact))
            .Select(s => s.Artifact!)
            .ToList();
        if (artifacts.Count > 0)
            return artifacts.Count == 1 ? artifacts[0] : CombineJsonValues(artifacts);

        var namedContents = run.ShardResults
            .OrderBy(s => s.Index)
            .SelectMany(s => s.Artifacts)
            .Where(a => !a.IsBinary && !string.IsNullOrWhiteSpace(a.Content))
            .Select(a => a.Content)
            .ToList();
        if (namedContents.Count == 0) return null;
        if (namedContents.Count == 1) return namedContents[0];
        return CombineJsonValues(namedContents);
    }

    /// <summary>The join's stdin input: every fan-out branch's primary output, in branch order,
    /// combined with the same convention <see cref="PrimaryOutput"/> uses for multiple shard
    /// artifacts — a raw JSON array whose elements are embedded as-is when they're already valid
    /// JSON, else JSON-encoded as a string. A branch with no output (e.g. it produced nothing)
    /// contributes a JSON <c>null</c>.</summary>
    internal static string? CombineOutputs(IReadOnlyList<string?> branchOutputs)
    {
        if (branchOutputs.Count == 0) return null;
        if (branchOutputs.Count == 1) return branchOutputs[0];
        return CombineJsonValues(branchOutputs.Select(o => o ?? "null"));
    }

    private static string CombineJsonValues(IEnumerable<string> values)
    {
        var parts = values.Select(v =>
        {
            try
            {
                using var doc = JsonDocument.Parse(v);
                return v; // already valid JSON — embed raw
            }
            catch
            {
                return JsonSerializer.Serialize(v); // plain text — embed as a JSON string
            }
        });
        return "[" + string.Join(",", parts) + "]";
    }

    /// <summary>
    /// The chain's step summary, attached to the <c>job.chain</c> span as a single tag when the run
    /// finishes. There's no way to tag stage/branch position onto each dispatched <c>job.run</c>
    /// activity from here without reaching into <c>RunJobHandler</c> (out of scope for this change),
    /// so the chain — which already owns this structure via <see cref="ChainRun"/> — publishes it
    /// itself; the Infrastructure collector parses this tag back into
    /// <see cref="Ports.ChainRunStepTelemetry"/> when it captures the stopped <c>job.chain</c> activity.
    /// </summary>
    private static string BuildStepsTelemetryJson(ChainRun chainRun)
        => JsonSerializer.Serialize(chainRun.Steps.Select(s => new
        {
            stageIndex = s.StageIndex,
            branchIndex = s.BranchIndex,
            jobId = s.JobId,
            jobName = s.JobName,
            runId = s.RunId,
            status = s.Status.ToString(),
            durationMs = s.StartedAt is { } started && s.FinishedAt is { } finished
                ? (finished - started).TotalMilliseconds
                : (double?)null,
        }));
}
