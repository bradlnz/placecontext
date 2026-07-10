using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Lists a project's triggers (schedule + event).</summary>
public sealed record ListTriggersQuery(Guid ProjectId) : IQuery<IReadOnlyList<TriggerView>>;
