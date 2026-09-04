using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class DropProjectViewHandler : ICommandHandler<DropProjectViewCommand, bool>
{
    private readonly IProjectDataStore _store;

    public DropProjectViewHandler(IProjectDataStore store) => _store = store;

    public async Task<bool> HandleAsync(DropProjectViewCommand command, CancellationToken ct = default)
    {
        var name = SaveProjectViewHandler.Ident(command.Name);
        await _store.ExecuteAsync(command.ProjectId, $"DROP VIEW IF EXISTS \"{name}\"", ct);
        return true;
    }
}
