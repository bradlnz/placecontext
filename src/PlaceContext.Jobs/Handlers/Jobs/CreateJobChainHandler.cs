using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class CreateJobChainHandler : ICommandHandler<CreateJobChainCommand, JobChainView>
{
    private readonly IJobChainRepository _chains;
    private readonly IJobRepository _jobs;
    private readonly IJobsUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateJobChainHandler(IJobChainRepository chains, IJobRepository jobs, IJobsUnitOfWork uow, IClock clock)
    {
        _chains = chains;
        _jobs = jobs;
        _uow = uow;
        _clock = clock;
    }

    public async Task<JobChainView> HandleAsync(CreateJobChainCommand command, CancellationToken ct = default)
    {
        var chain = command.Stages is { Count: > 0 } stages
            ? JobChain.Create(command.ProjectId, command.Name, command.Description,
                ToStages(stages, command.StageGates, command.StageActions), _clock.UtcNow)
            : JobChain.Create(command.ProjectId, command.Name, command.Description, command.StepJobIds, _clock.UtcNow);
        await ValidateStepsAsync(_jobs, chain, ct);
        await _chains.AddAsync(chain, ct);
        await _uow.SaveChangesAsync(ct);
        return await JobChainMapper.ToViewAsync(chain, _jobs, ct);
    }

    internal static List<ChainStage> ToStages(IReadOnlyList<IReadOnlyList<Guid>> stages,
        IReadOnlyList<ChainGate?>? gates = null,
        IReadOnlyList<ChainAction?>? actions = null)
        => stages.Select((s, i) => new ChainStage(
            s,
            gate: gates is not null && i < gates.Count ? gates[i] : null,
            action: actions is not null && i < actions.Count ? actions[i] : null)).ToList();

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
