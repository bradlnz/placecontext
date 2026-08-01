namespace PlaceContext.Application.Ports;

/// <summary>The one-time plaintext result returned when an artifact share link is created or rotated.</summary>
public sealed record ArtifactShareCreated(
    string Token,
    string TokenPrefix,
    DateTimeOffset ExpiresAt);

/// <summary>Tenant-scoped metadata for an artifact's current public share credential.</summary>
public sealed record ArtifactShareStatus(
    bool IsActive,
    string TokenPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastAccessedAt);

/// <summary>Storage coordinates disclosed internally after a public bearer token is validated.</summary>
public sealed record SharedArtifact(
    string Title,
    string Bucket,
    string ObjectKey,
    string ContentType);

/// <summary>
/// Manages expiring, revocable bearer credentials for public artifact access. Implementations must
/// persist only a one-way token digest; the plaintext token is returned only at creation time.
/// </summary>
public interface IArtifactShareTokenService
{
    Task<ArtifactShareCreated> CreateOrRotateAsync(
        Guid artifactId,
        Guid createdByUserId,
        int lifetimeDays,
        CancellationToken ct = default);

    Task<ArtifactShareStatus?> GetStatusAsync(Guid artifactId, CancellationToken ct = default);
    Task<bool> RevokeAsync(Guid artifactId, CancellationToken ct = default);
    Task<SharedArtifact?> ResolveAsync(string token, CancellationToken ct = default);
}
