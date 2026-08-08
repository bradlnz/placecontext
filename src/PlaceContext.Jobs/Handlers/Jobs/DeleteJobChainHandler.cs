using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteJobChainHandler : ICommandHandler<DeleteJobChainCommand, bool>
{
    private readonly IJobChainRepository _chains;
    private readonly IJobsUnitOfWork _uow;

    public DeleteJobChainHandler(IJobChainRepository chains, IJobsUnitOfWork uow)
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
