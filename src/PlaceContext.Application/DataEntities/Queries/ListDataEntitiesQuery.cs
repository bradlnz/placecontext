using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed record ListDataEntitiesQuery(Guid ProjectId) : IQuery<IReadOnlyList<DataEntityView>>;
