using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Auth;

/// <summary>
/// Personal API tokens: random 32-byte secrets, stored as SHA-256 hex. Lookup is by exact hash
/// (unique index) so validation is O(1) and constant-time on the hash compare path.
/// </summary>
public sealed class UserApiTokenService : IUserApiTokenService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _tenant;

    public UserApiTokenService(AppDbContext db, ICurrentUser currentUser, ICurrentTenant tenant)
    {
        _db = db;
        _currentUser = currentUser;
        _tenant = tenant;
    }

    public async Task<CreatedUserApiToken> CreateAsync(string name, TimeSpan? lifetime, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated)
            throw new InvalidOperationException("Sign in to create an API token.");
        name = (name ?? "").Trim();
        if (name.Length == 0)
            throw new ArgumentException("Token name is required.", nameof(name));
        if (name.Length > 80)
            throw new ArgumentException("Token name is too long (max 80 characters).", nameof(name));

        var rawBytes = RandomNumberGenerator.GetBytes(32);
        var raw = "pct_" + Convert.ToHexString(rawBytes).ToLowerInvariant();
        var hash = Hash(raw);
        var now = DateTimeOffset.UtcNow;
        var row = new UserApiTokenRow
        {
            Id = Guid.NewGuid(),
            UserId = _currentUser.UserId,
            Name = name,
            TokenHash = hash,
            TokenPrefix = raw[..11], // "pct_" + 8 hex chars
            CreatedAt = now,
            ExpiresAt = lifetime is { } t && t > TimeSpan.Zero ? now.Add(t) : null,
        };
        await _db.UserApiTokens.AddAsync(row, ct);
        await _db.SaveChangesAsync(ct);
        return new CreatedUserApiToken(row.Id, row.Name, raw, row.TokenPrefix, row.CreatedAt, row.ExpiresAt);
    }

    public async Task<IReadOnlyList<UserApiTokenView>> ListMineAsync(CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Array.Empty<UserApiTokenView>();
        var rows = await _db.UserApiTokens.AsNoTracking()
            .Where(t => t.UserId == _currentUser.UserId && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(r => new UserApiTokenView(r.Id, r.Name, r.TokenPrefix, r.CreatedAt, r.LastUsedAt, r.ExpiresAt)).ToList();
    }

    public async Task<bool> RevokeAsync(Guid tokenId, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return false;
        var row = await _db.UserApiTokens.FirstOrDefaultAsync(
            t => t.Id == tokenId && t.UserId == _currentUser.UserId && t.RevokedAt == null, ct);
        if (row is null) return false;
        row.RevokedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<AuthUser?> ValidateAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || !rawToken.StartsWith("pct_", StringComparison.Ordinal))
            return null;

        var hash = Hash(rawToken);
        var row = await _db.UserApiTokens.FirstOrDefaultAsync(
            t => t.TokenHash == hash && t.RevokedAt == null, ct);
        if (row is null) return null;
        if (row.ExpiresAt is { } exp && exp < DateTimeOffset.UtcNow) return null;

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == row.UserId, ct);
        if (user is null) return null;

        row.LastUsedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var role = Enum.TryParse<UserRole>(user.Role, out var r) ? r : UserRole.Member;
        return new AuthUser(user.Id, user.TenantId, user.Email, user.DisplayName, role);
    }

    private static string Hash(string raw)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
}
