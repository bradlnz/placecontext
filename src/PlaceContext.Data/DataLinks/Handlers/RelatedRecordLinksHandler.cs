using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed class RelatedRecordLinksHandler
    : IQueryHandler<RelatedRecordLinksQuery, IReadOnlyList<RecordLink>>
{
    private readonly IRecordLinkStore _store;

    public RelatedRecordLinksHandler(IRecordLinkStore store) => _store = store;

    public Task<IReadOnlyList<RecordLink>> HandleAsync(
        RelatedRecordLinksQuery query,
        CancellationToken ct = default)
        => _store.RelatedAsync(
            query.ProjectId,
            query.TableName,
            query.RowKey,
            query.Take,
            ct);
}
