using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class DeleteSavedQueryHandler
    : ICommandHandler<DeleteSavedQueryCommand, bool>
{
    private readonly ISavedQueryStore _store;
    public DeleteSavedQueryHandler(ISavedQueryStore store) => _store = store;

    public Task<bool> HandleAsync(
        DeleteSavedQueryCommand command, CancellationToken ct = default)
        => _store.DeleteAsync(command.QueryId, ct);
}
