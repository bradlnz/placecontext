using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed class ListRecordLinkGroupsHandler
    : IQueryHandler<ListRecordLinkGroupsQuery, IReadOnlyList<RecordLinkGroup>>
{
    private readonly IRecordLinkStore _store;

    public ListRecordLinkGroupsHandler(IRecordLinkStore store) => _store = store;

    public Task<IReadOnlyList<RecordLinkGroup>> HandleAsync(
        ListRecordLinkGroupsQuery query,
        CancellationToken ct = default)
        => _store.GroupsAsync(query.ProjectId, query.Take, ct);
}
