using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Runs a chain as a staged pipeline: dispatches each step as a normal <see cref="RunJobCommand"/>
/// and threads the previous run's primary output into the next step's input payload. Stops at the
/// first failed step; a partial step downgrades the chain to Partial but the pipeline continues.
/// The run is persisted the moment it starts and saved on every stage transition (pending →
/// running → outcome), so the portal can watch the pipeline progress live and keep a history.
/// </summary>
public sealed class RunJobChainHandler : ICommandHandler<RunJobChainCommand, ChainRunView>
{
    private readonly IJobChainRepository _chains;
    private readonly IJobRepository _jobs;
    private readonly IChainRunRepository _runs;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly IDispatcher _dispatcher;

    public RunJobChainHandler(IJobChainRepository chains, IJobRepository jobs, IChainRunRepository runs,
        IUnitOfWork uow, IClock clock, IDispatcher dispatcher)
    {
        _chains = chains;
        _jobs = jobs;
        _runs = runs;
        _uow = uow;
        _clock = clock;
        _dispatcher = dispatcher;
    }

    public async Task<ChainRunView> HandleAsync(RunJobChainCommand command, CancellationToken ct = default)
    {
        var chain = await _chains.GetByIdAsync(command.ChainId, ct)
            ?? throw new InvalidOperationException($"Chain {command.ChainId} not found.");

        // Snapshot the step jobs up front — names go on the run so its history stands alone.
        var stepJobs = new List<Job?>(chain.StepJobIds.Count);
        foreach (var jobId in chain.StepJobIds)
            stepJobs.Add(await _jobs.GetByIdAsync(jobId, ct));
        var names = stepJobs.Select((j, i) => j?.Name ?? "(deleted)").ToList();

        var chainRun = ChainRun.Start(chain, names, _clock.UtcNow, command.ChainRunId);
        await _runs.AddAsync(chainRun, ct);
        await _uow.SaveChangesAsync(ct);

        var status = ChainRunStatus.Succeeded;
        var payload = command.InputPayload; // first step: caller's payload, or the job's stored shards

        for (var i = 0; i < chain.StepJobIds.Count; i++)
        {
            if (stepJobs[i] is null)
            {
                chainRun.MarkStepFinished(i, runId: null, ChainStepStatus.Failed, _clock.UtcNow);
                status = ChainRunStatus.Failed;
                break;
            }

            // Pre-allocate the step's run id and record it before dispatching, so the live pipeline
            // (and the run-status watcher) can address the step's run while it is still executing.
            var stepRunId = Guid.NewGuid();
            chainRun.MarkStepRunning(i, stepRunId, _clock.UtcNow);
            await SaveProgressAsync(chainRun, ct);

            var stepPayload = command.StepPayloadOverrides is { } overrides && overrides.TryGetValue(i, out var args)
                ? MergePayload(payload, args)
                : payload;
            var run = await _dispatcher.Send(new RunJobCommand(chain.StepJobIds[i], stepPayload, stepRunId), ct);
            chainRun.MarkStepFinished(i, run.Id, ParseStepOutcome(run.Status), _clock.UtcNow);
            await SaveProgressAsync(chainRun, ct);

            if (run.Status == "Failed")
            {
                status = ChainRunStatus.Failed;
                break;
            }
            if (run.Status == "Partial") status = ChainRunStatus.Partial;
            payload = PrimaryOutput(run);
        }

        chainRun.Complete(status, payload, _clock.UtcNow);
        await SaveProgressAsync(chainRun, ct);

        return ChainRunMapper.ToView(chainRun);
    }

    // Fold collected step parameters over the chained input: two JSON objects merge shallowly
    // (parameter values win); anything else keeps the chained input under "previous" beside them.
    private static string MergePayload(string? chained, string args)
    {
        if (string.IsNullOrWhiteSpace(chained)) return args;
        try
        {
            var argsNode = System.Text.Json.Nodes.JsonNode.Parse(args) as System.Text.Json.Nodes.JsonObject
                ?? throw new System.Text.Json.JsonException();
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
        _ => ChainStepStatus.Failed,
    };

    /// <summary>The run's primary output, as the next step's stdin payload: the reduce artifact when
    /// present (the final aggregate), a lone shard's artifact as-is, else a JSON array of the shard
    /// artifacts (raw values when they are JSON, JSON-encoded strings otherwise).</summary>
    internal static string? PrimaryOutput(JobRunDetailView run)
    {
        if (run.ReduceResult?.Artifact is { Length: > 0 } reduce) return reduce;

        var artifacts = run.ShardResults
            .OrderBy(s => s.Index)
            .Where(s => !string.IsNullOrWhiteSpace(s.Artifact))
            .Select(s => s.Artifact!)
            .ToList();
        if (artifacts.Count == 0) return null;
        if (artifacts.Count == 1) return artifacts[0];

        var parts = artifacts.Select(a =>
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(a);
                return a; // already valid JSON — embed raw
            }
            catch
            {
                return System.Text.Json.JsonSerializer.Serialize(a); // plain text — embed as a JSON string
            }
        });
        return "[" + string.Join(",", parts) + "]";
    }
}
