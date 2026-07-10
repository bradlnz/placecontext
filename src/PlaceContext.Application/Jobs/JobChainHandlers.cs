using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class CreateJobChainHandler : ICommandHandler<CreateJobChainCommand, JobChainView>
{
    private readonly IJobChainRepository _chains;
    private readonly IJobRepository _jobs;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateJobChainHandler(IJobChainRepository chains, IJobRepository jobs, IUnitOfWork uow, IClock clock)
    {
        _chains = chains;
        _jobs = jobs;
        _uow = uow;
        _clock = clock;
    }

    public async Task<JobChainView> HandleAsync(CreateJobChainCommand command, CancellationToken ct = default)
    {
        var chain = JobChain.Create(command.ProjectId, command.Name, command.Description,
            command.StepJobIds, _clock.UtcNow);
        await ValidateStepsAsync(_jobs, chain, ct);
        await _chains.AddAsync(chain, ct);
        await _uow.SaveChangesAsync(ct);
        return await JobChainMapper.ToViewAsync(chain, _jobs, ct);
    }

    /// <summary>Every step must reference an existing job in the chain's project — a typo'd or foreign
    /// job id should fail at definition time, not at run time.</summary>
    internal static async Task ValidateStepsAsync(IJobRepository jobs, JobChain chain, CancellationToken ct)
    {
        foreach (var jobId in chain.StepJobIds.Distinct())
        {
            var job = await jobs.GetByIdAsync(jobId, ct)
                ?? throw new InvalidOperationException($"Chain step references unknown job {jobId}.");
            if (job.ProjectId != chain.ProjectId)
                throw new InvalidOperationException($"Job '{job.Name}' belongs to a different project.");
        }
    }
}

public sealed class UpdateJobChainHandler : ICommandHandler<UpdateJobChainCommand, JobChainView>
{
    private readonly IJobChainRepository _chains;
    private readonly IJobRepository _jobs;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public UpdateJobChainHandler(IJobChainRepository chains, IJobRepository jobs, IUnitOfWork uow, IClock clock)
    {
        _chains = chains;
        _jobs = jobs;
        _uow = uow;
        _clock = clock;
    }

    public async Task<JobChainView> HandleAsync(UpdateJobChainCommand command, CancellationToken ct = default)
    {
        var chain = await _chains.GetByIdAsync(command.ChainId, ct)
            ?? throw new InvalidOperationException($"Chain {command.ChainId} not found.");
        chain.Update(command.Name, command.Description, command.StepJobIds, _clock.UtcNow);
        await CreateJobChainHandler.ValidateStepsAsync(_jobs, chain, ct);
        await _chains.UpdateAsync(chain, ct);
        await _uow.SaveChangesAsync(ct);
        return await JobChainMapper.ToViewAsync(chain, _jobs, ct);
    }
}

public sealed class DeleteJobChainHandler : ICommandHandler<DeleteJobChainCommand, bool>
{
    private readonly IJobChainRepository _chains;
    private readonly IUnitOfWork _uow;

    public DeleteJobChainHandler(IJobChainRepository chains, IUnitOfWork uow)
    {
        _chains = chains;
        _uow = uow;
    }

    public async Task<bool> HandleAsync(DeleteJobChainCommand command, CancellationToken ct = default)
    {
        var existing = await _chains.GetByIdAsync(command.ChainId, ct);
        if (existing is null) return false;
        await _chains.RemoveAsync(command.ChainId, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class ListJobChainsHandler : IQueryHandler<ListJobChainsQuery, IReadOnlyList<JobChainView>>
{
    private readonly IJobChainRepository _chains;
    private readonly IJobRepository _jobs;

    public ListJobChainsHandler(IJobChainRepository chains, IJobRepository jobs)
    {
        _chains = chains;
        _jobs = jobs;
    }

    public async Task<IReadOnlyList<JobChainView>> HandleAsync(ListJobChainsQuery query, CancellationToken ct = default)
    {
        var chains = await _chains.ListForProjectAsync(query.ProjectId, ct);
        var views = new List<JobChainView>(chains.Count);
        foreach (var chain in chains)
            views.Add(await JobChainMapper.ToViewAsync(chain, _jobs, ct));
        return views;
    }
}

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

internal static class JobChainMapper
{
    public static async Task<JobChainView> ToViewAsync(JobChain chain, IJobRepository jobs, CancellationToken ct)
    {
        var steps = new List<JobChainStepView>(chain.StepJobIds.Count);
        foreach (var jobId in chain.StepJobIds)
        {
            var job = await jobs.GetByIdAsync(jobId, ct);
            steps.Add(new JobChainStepView(jobId, job?.Name ?? "(deleted)"));
        }
        return new JobChainView(chain.Id, chain.ProjectId, chain.Name, chain.Description, steps, chain.UpdatedAt);
    }
}
