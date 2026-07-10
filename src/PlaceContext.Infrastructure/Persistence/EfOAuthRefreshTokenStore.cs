using System.Security.Cryptography;
using System.Text;
using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>
/// Persists refresh tokens hashed at rest (a database leak leaks no usable tokens). Rotation deletes
/// the presented row with an atomic conditional delete, so two concurrent refreshes with the same
/// token can never both succeed.
/// </summary>
public sealed class EfOAuthRefreshTokenStore : IOAuthRefreshTokenStore
{
    /// <summary>Refresh lifetime — how long an MCP client can sit idle before the browser flow is
    /// needed again. Every rotation restarts the clock, so active clients renew indefinitely.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private readonly AppDbContext _db;
    public EfOAuthRefreshTokenStore(AppDbContext db) => _db = db;

    public async Task<OAuthRefreshGrant> IssueAsync(string clientId, Guid userId, Guid tenantId, string role, string scope,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        // Opportunistic purge — keeps the table from accumulating grants of clients that never came back.
        await _db.OAuthRefreshTokens.Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(ct);

        var token = "pcr_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var row = new OAuthRefreshTokenRow
        {
            TokenHash = Hash(token),
            ClientId = clientId,
            UserId = userId,
            TenantId = tenantId,
            Role = role,
            Scope = scope,
            ExpiresAt = now + Lifetime,
            CreatedAt = now,
        };
        await _db.OAuthRefreshTokens.AddAsync(row, ct);
        await _db.SaveChangesAsync(ct);
        return new OAuthRefreshGrant(token, clientId, userId, tenantId, role, scope, row.ExpiresAt);
    }

    public async Task<OAuthRefreshGrant?> RotateAsync(string token, string clientId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var hash = Hash(token);

        var row = await _db.OAuthRefreshTokens.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (row is null || row.ClientId != clientId) return null;

        // Single use: whoever deletes the row wins; a raced or replayed token gets nothing.
        var consumed = await _db.OAuthRefreshTokens.Where(x => x.TokenHash == hash).ExecuteDeleteAsync(ct);
        if (consumed == 0 || row.ExpiresAt <= DateTimeOffset.UtcNow) return null;

        return await IssueAsync(row.ClientId, row.UserId, row.TenantId, row.Role, row.Scope, ct);
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
}
