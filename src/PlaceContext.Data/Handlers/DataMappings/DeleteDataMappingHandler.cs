using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteDataMappingHandler : ICommandHandler<DeleteDataMappingCommand, bool>
{
    private readonly IDataMappingRepository _mappings;
    private readonly IDataUnitOfWork _uow;

    public DeleteDataMappingHandler(IDataMappingRepository mappings, IDataUnitOfWork uow)
    {
        _mappings = mappings;
        _uow = uow;
    }

    public async Task<bool> HandleAsync(DeleteDataMappingCommand command, CancellationToken ct = default)
    {
        await _mappings.DeleteAsync(command.MappingId, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
