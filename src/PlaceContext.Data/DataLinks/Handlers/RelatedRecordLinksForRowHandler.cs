using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed class RelatedRecordLinksForRowHandler
    : IQueryHandler<RelatedRecordLinksForRowQuery, IReadOnlyList<RecordLink>>
{
    private readonly RecordLinkService _links;

    public RelatedRecordLinksForRowHandler(RecordLinkService links) => _links = links;

    public Task<IReadOnlyList<RecordLink>> HandleAsync(
        RelatedRecordLinksForRowQuery query,
        CancellationToken ct = default)
        => _links.RelatedForRowAsync(
            query.ProjectId,
            query.TableName,
            query.Values,
            query.Take,
            ct);
}
