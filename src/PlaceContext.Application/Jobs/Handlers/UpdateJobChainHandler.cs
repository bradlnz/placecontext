using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

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
        if (command.ExpectedUpdatedAt is { } expected && chain.UpdatedAt != expected)
            throw new InvalidOperationException("This workflow was changed since you opened it. Reload before saving to avoid overwriting newer edits.");
        var updatedAt = _clock.UtcNow > chain.UpdatedAt
            ? _clock.UtcNow : chain.UpdatedAt.AddTicks(10);
        if (command.Stages is { Count: > 0 } stages)
            chain.Update(command.Name, command.Description,
                CreateJobChainHandler.ToStages(stages, command.StageGates, command.StageActions), updatedAt);
        else
            chain.Update(command.Name, command.Description, command.StepJobIds, updatedAt);
        await CreateJobChainHandler.ValidateStepsAsync(_jobs, chain, ct);
        await _chains.UpdateAsync(chain, ct);
        await _uow.SaveChangesAsync(ct);
        return await JobChainMapper.ToViewAsync(chain, _jobs, ct);
    }
}
