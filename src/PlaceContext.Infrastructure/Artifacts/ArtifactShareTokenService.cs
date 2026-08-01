using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Artifacts;

/// <summary>
/// EF-backed public artifact shares. Tenant filters protect management operations; resolution
/// deliberately crosses those filters only after a high-entropy bearer token digest matches.
/// </summary>
public sealed class ArtifactShareTokenService : IArtifactShareTokenService
{
    public const int DefaultLifetimeDays = 7;
    public const int MaximumLifetimeDays = 30;

    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public ArtifactShareTokenService(AppDbContext db, IClock clock)
        => (_db, _clock) = (db, clock);

    public async Task<ArtifactShareCreated> CreateOrRotateAsync(
        Guid artifactId,
        Guid createdByUserId,
        int lifetimeDays,
        CancellationToken ct = default)
    {
        if (lifetimeDays is < 1 or > MaximumLifetimeDays)
            throw new ArgumentOutOfRangeException(
                nameof(lifetimeDays), $"Share links must expire within 1–{MaximumLifetimeDays} days.");

        if (artifactId == Guid.Empty
            || !await _db.RunArtifacts.AsNoTracking().AnyAsync(item => item.Id == artifactId, ct))
            throw new InvalidOperationException("Artifact not found.");

        var token = NewToken();
        var now = _clock.UtcNow;
        var row = await _db.ArtifactShareTokens
            .SingleOrDefaultAsync(item => item.ArtifactId == artifactId, ct);
        if (row is null)
        {
            row = new ArtifactShareTokenRow
            {
                Id = Guid.NewGuid(),
                ArtifactId = artifactId,
            };
            await _db.ArtifactShareTokens.AddAsync(row, ct);
        }

        row.TokenHash = Hash(token);
        row.TokenPrefix = token[..Math.Min(15, token.Length)] + "…";
        row.CreatedByUserId = createdByUserId;
        row.CreatedAt = now;
        row.ExpiresAt = now.AddDays(lifetimeDays);
        row.RevokedAt = null;
        row.LastAccessedAt = null;
        await _db.SaveChangesAsync(ct);

        return new ArtifactShareCreated(token, row.TokenPrefix, row.ExpiresAt);
    }

    public async Task<ArtifactShareStatus?> GetStatusAsync(
        Guid artifactId,
        CancellationToken ct = default)
    {
        var row = await _db.ArtifactShareTokens.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ArtifactId == artifactId, ct);
        return row is null ? null : new ArtifactShareStatus(
            row.RevokedAt is null && row.ExpiresAt > _clock.UtcNow,
            row.TokenPrefix,
            row.CreatedAt,
            row.ExpiresAt,
            row.LastAccessedAt);
    }

    public async Task<bool> RevokeAsync(Guid artifactId, CancellationToken ct = default)
    {
        var row = await _db.ArtifactShareTokens
            .SingleOrDefaultAsync(item => item.ArtifactId == artifactId, ct);
        if (row is null || row.RevokedAt is not null) return false;
        row.RevokedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<SharedArtifact?> ResolveAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128) return null;
        token = token.Trim();
        if (!token.StartsWith("pc_art_", StringComparison.Ordinal)) return null;

        var hash = Hash(token);
        var now = _clock.UtcNow;
        var share = await _db.ArtifactShareTokens.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item =>
                item.TokenHash == hash
                && item.RevokedAt == null
                && item.ExpiresAt > now, ct);
        if (share is null) return null;

        var artifact = await _db.RunArtifacts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.Id == share.ArtifactId && item.TenantId == share.TenantId, ct);
        if (artifact is null) return null;

        share.LastAccessedAt = now;
        await _db.SaveChangesAsync(ct);
        return new SharedArtifact(
            artifact.Title,
            artifact.Bucket,
            artifact.ObjectKey,
            artifact.ContentType);
    }

    internal static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var encoded = Convert.ToBase64String(bytes).TrimEnd('=')
            .Replace('+', '-').Replace('/', '_');
        return "pc_art_" + encoded;
    }
}
