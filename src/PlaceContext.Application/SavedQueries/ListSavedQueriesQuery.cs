using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record ListSavedQueriesQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<SavedQueryRecord>>;
