namespace PlaceContext.Application.Features;

/// <summary>Runs whose output was tagged with this entity key — the relation tree, queried.</summary>
public sealed record TaggedRunsQuery(Guid EntityId, string Key) : Cqrs.IQuery<IReadOnlyList<Guid>>;

/// <summary>Every run tagged against an entity — the section-level rollup of its relation tree.</summary>
public sealed record EntityRunsQuery(Guid EntityId) : Cqrs.IQuery<IReadOnlyList<Guid>>;

/// <summary>The entity's tag pairs — the concrete edges between its records and runs.</summary>
public sealed record EntityTagPairsQuery(Guid EntityId) : Cqrs.IQuery<IReadOnlyList<EntityTagPair>>;
