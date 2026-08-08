namespace PlaceContext.Application.Features;

/// <summary>Every run tagged against an entity — the section-level rollup of its relation tree.</summary>
public sealed record EntityRunsQuery(Guid EntityId) : Cqrs.IQuery<IReadOnlyList<Guid>>;
