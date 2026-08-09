using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed record RelatedRecordLinksForRowQuery(
    Guid ProjectId,
    string TableName,
    IReadOnlyDictionary<string, string?> Values,
    int Take = 30) : IQuery<IReadOnlyList<RecordLink>>;
