namespace PlaceContext.Application.Features;

/// <summary>One edge of the run↔entity relation tree: this run's output carried this entity key.</summary>
public sealed record EntityTag(Guid ProjectId, Guid EntityId, string EntityName, string Key, Guid RunId, Guid JobId);

/// <summary>A persisted tag edge, as consumed by graph views: key value ⇄ run (and its job).</summary>
public sealed record EntityTagPair(string Key, Guid RunId, Guid JobId);

/// <summary>A tag-index search hit: an entity record key somewhere in the workspace's data graph.</summary>
public sealed record EntityTagHit(Guid ProjectId, string EntityName, string Key);
