using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class ListSavedQueriesHandler
    : IQueryHandler<ListSavedQueriesQuery, IReadOnlyList<SavedQueryRecord>>
{
    private readonly ISavedQueryStore _store;
    public ListSavedQueriesHandler(ISavedQueryStore store) => _store = store;

    public Task<IReadOnlyList<SavedQueryRecord>> HandleAsync(
        ListSavedQueriesQuery query, CancellationToken ct = default)
        => _store.ListAsync(query.ProjectId, ct);
}
