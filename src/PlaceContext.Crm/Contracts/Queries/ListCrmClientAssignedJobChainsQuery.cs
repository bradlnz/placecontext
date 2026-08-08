using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;
public sealed record ListCrmClientAssignedJobChainsQuery(
    Guid ClientId,
    Guid ProjectId) : IQuery<IReadOnlyList<Guid>>;
