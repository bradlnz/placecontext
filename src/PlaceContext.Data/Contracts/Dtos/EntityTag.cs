namespace PlaceContext.Application.Features;

/// <summary>One edge of the run↔entity relation tree: this run's output carried this entity key.</summary>
public sealed record EntityTag(Guid ProjectId, Guid EntityId, string EntityName, string Key, Guid RunId, Guid JobId);
