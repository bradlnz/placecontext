using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Lists all event types: the reserved built-ins plus this workspace's user-defined ones.</summary>
public sealed record ListEventTypesQuery() : IQuery<IReadOnlyList<EventTypeView>>;
