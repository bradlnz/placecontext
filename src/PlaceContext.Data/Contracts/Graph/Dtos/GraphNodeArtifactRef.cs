namespace PlaceContext.Application.Dtos;

/// <summary>Artifact metadata attached to a graph node so the canvas can render it inline.</summary>
public sealed record GraphNodeArtifactRef(
    Guid Id,
    Guid RunId,
    string Kind,
    string Title,
    string ContentType,
    DateTimeOffset CreatedAt);
