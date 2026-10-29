namespace PlaceContext.Application.Features;

/// <summary>A tag-index search hit: an entity record key somewhere in the workspace's data graph.</summary>
public sealed record EntityTagHit(Guid ProjectId, string EntityName, string Key);
