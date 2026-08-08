using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteTriggerHandler : ICommandHandler<DeleteTriggerCommand, bool>
{
    private readonly IJobTriggerRepository _triggers;
    private readonly IJobsUnitOfWork _uow;

    public DeleteTriggerHandler(IJobTriggerRepository triggers, IJobsUnitOfWork uow)
    {
        _triggers = triggers;
        _uow = uow;
    }

    public async Task<bool> HandleAsync(DeleteTriggerCommand command, CancellationToken ct = default)
    {
        var existing = await _triggers.GetByIdAsync(command.TriggerId, ct);
        if (existing is null) return false;

        await _triggers.RemoveAsync(command.TriggerId, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
