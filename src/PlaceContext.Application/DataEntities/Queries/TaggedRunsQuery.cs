namespace PlaceContext.Application.Features;

/// <summary>Runs whose output was tagged with this entity key — the relation tree, queried.</summary>
public sealed record TaggedRunsQuery(Guid EntityId, string Key) : Cqrs.IQuery<IReadOnlyList<Guid>>;
