namespace PlaceContext.Application.Ports;

/// <summary>One semantic search hit over indexed project content.</summary>
public sealed record ContentSearchHit(
    string Kind,
    string SourceKey,
    string Text,
    double Score,
    DateTimeOffset CreatedAt);
