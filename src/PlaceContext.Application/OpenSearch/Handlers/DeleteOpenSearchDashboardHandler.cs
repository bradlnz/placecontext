using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class DeleteOpenSearchDashboardHandler
    : ICommandHandler<DeleteOpenSearchDashboardCommand, bool>
{
    private readonly IOpenSearchDashboardStore _store;
    public DeleteOpenSearchDashboardHandler(IOpenSearchDashboardStore store) => _store = store;
    public Task<bool> HandleAsync(
        DeleteOpenSearchDashboardCommand command, CancellationToken ct = default)
        => _store.DeleteAsync(command.DashboardId, ct);
}
