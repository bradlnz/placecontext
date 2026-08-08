namespace PlaceContext.Application.Ports;

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
