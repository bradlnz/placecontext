namespace PlaceContext.Application.Features;

/// <summary>A persisted tag edge, as consumed by graph views: key value ⇄ run (and its job).</summary>
public sealed record EntityTagPair(string Key, Guid RunId, Guid JobId);
