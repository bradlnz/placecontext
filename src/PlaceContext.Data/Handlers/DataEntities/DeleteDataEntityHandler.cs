using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class DeleteDataEntityHandler : ICommandHandler<DeleteDataEntityCommand, bool>
{
    private readonly IDataEntityRepository _entities;
    private readonly IDataUnitOfWork _uow;

    public DeleteDataEntityHandler(IDataEntityRepository entities, IDataUnitOfWork uow)
    {
        _entities = entities;
        _uow = uow;
    }

    public async Task<bool> HandleAsync(DeleteDataEntityCommand command, CancellationToken ct = default)
    {
        await _entities.DeleteAsync(command.EntityId, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
