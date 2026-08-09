using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed record ListRecordLinkGroupsQuery(Guid ProjectId, int Take = 50)
    : IQuery<IReadOnlyList<RecordLinkGroup>>;
