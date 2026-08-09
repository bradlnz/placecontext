using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed record RelatedRecordLinksQuery(
    Guid ProjectId,
    string TableName,
    string RowKey,
    int Take = 20) : IQuery<IReadOnlyList<RecordLink>>;
