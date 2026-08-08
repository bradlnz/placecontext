using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>A stored artifact file with enough context to browse and open it (the file viewer).</summary>
public sealed record ArtifactFileView(
    Guid Id,
    Guid RunId,
    Guid JobId,
    Guid ProjectId,
    string Kind,
    string Title,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt);
