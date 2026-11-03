using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Search;
namespace PlaceContext.Application.Features;

public sealed class DeleteSearchDashboardHandler
    : ICommandHandler<DeleteSearchDashboardCommand, bool>
{
    private readonly ISearchDashboardStore _store;
    public DeleteSearchDashboardHandler(ISearchDashboardStore store) => _store = store;
    public Task<bool> HandleAsync(
        DeleteSearchDashboardCommand command, CancellationToken ct = default)
        => _store.DeleteAsync(command.DashboardId, ct);
}
