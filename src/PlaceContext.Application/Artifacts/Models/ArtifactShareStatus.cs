namespace PlaceContext.Application.Artifacts;

/// <summary>Tenant-scoped metadata for an artifact's current public share credential.</summary>
public sealed record ArtifactShareStatus(
    bool IsActive,
    string TokenPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastAccessedAt);
