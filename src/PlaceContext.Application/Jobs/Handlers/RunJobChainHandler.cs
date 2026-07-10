using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>
/// Runs a chain: dispatches each step as a normal <see cref="RunJobCommand"/> and threads the
/// previous run's primary output into the next step's input payload. Stops at the first failed step;
/// a partial step downgrades the chain to Partial but the pipeline continues.
/// </summary>
public sealed class RunJobChainHandler : ICommandHandler<RunJobChainCommand, ChainRunView>
{
    private readonly IJobChainRepository _chains;
    private readonly IJobRepository _jobs;
    private readonly IDispatcher _dispatcher;

    public RunJobChainHandler(IJobChainRepository chains, IJobRepository jobs, IDispatcher dispatcher)
    {
        _chains = chains;
        _jobs = jobs;
        _dispatcher = dispatcher;
    }

    public async Task<ChainRunView> HandleAsync(RunJobChainCommand command, CancellationToken ct = default)
    {
        var chain = await _chains.GetByIdAsync(command.ChainId, ct)
            ?? throw new InvalidOperationException($"Chain {command.ChainId} not found.");

        var steps = new List<ChainStepRunView>(chain.StepJobIds.Count);
        var status = "Succeeded";
        var payload = command.InputPayload; // first step: caller's payload, or the job's stored shards

        for (var i = 0; i < chain.StepJobIds.Count; i++)
        {
            var jobId = chain.StepJobIds[i];
            var job = await _jobs.GetByIdAsync(jobId, ct);
            if (job is null)
            {
                steps.Add(new ChainStepRunView(i, jobId, "(deleted)", Guid.Empty, "Failed"));
                status = "Failed";
                break;
            }

            var run = await _dispatcher.Send(new RunJobCommand(jobId, payload), ct);
            steps.Add(new ChainStepRunView(i, jobId, job.Name, run.Id, run.Status));

            if (run.Status == "Failed")
            {
                status = "Failed";
                break;
            }
            if (run.Status == "Partial") status = "Partial";
            payload = PrimaryOutput(run);
        }

        return new ChainRunView(chain.Id, chain.Name, status, steps, payload);
    }

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
