namespace PlaceContext.Application.Features;

/// <summary>The entity's tag pairs — the concrete edges between its records and runs.</summary>
public sealed record EntityTagPairsQuery(Guid EntityId) : Cqrs.IQuery<IReadOnlyList<EntityTagPair>>;
