namespace PlaceContext.Application.Features;

/// <summary>A post-job output stored for a run, surfaced as an openable link in the portal/TUI.</summary>
public sealed record RunArtifactLinkView(
    Guid Id,
    Guid RunId,
    PlaceContext.Domain.ValueObjects.PostJobActionKind Kind,
    string Title,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt);
