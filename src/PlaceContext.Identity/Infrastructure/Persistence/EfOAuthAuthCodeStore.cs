using System.Security.Cryptography;
using System.Text;
using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Identity.Infrastructure.Persistence;

/// <summary>
/// Persists authorization codes hashed at rest (a database leak leaks no usable codes). Codes are
/// single-use: <c>TakeAsync</c> atomically deletes the row so a replay or race gets nothing.
/// </summary>
public sealed class EfOAuthAuthCodeStore : IOAuthAuthCodeStore
{
    private readonly IdentityDbContext _db;
    public EfOAuthAuthCodeStore(IdentityDbContext db) => _db = db;

    public async Task SaveAsync(AuthCode code, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _db.OAuthAuthCodes.Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(ct);

        var row = new OAuthAuthCodeRow
        {
            CodeHash = Hash(code.Code),
            ClientId = code.ClientId,
            RedirectUri = code.RedirectUri,
            CodeChallenge = code.CodeChallenge,
            UserId = code.UserId,
            TenantId = code.TenantId,
            Role = code.Role,
            Scope = code.Scope,
            ExpiresAt = code.Expires,
        };
        await _db.OAuthAuthCodes.AddAsync(row, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<AuthCode?> TakeAsync(string code, DateTimeOffset now, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(code)) return null;
        var hash = Hash(code);

        var row = await _db.OAuthAuthCodes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CodeHash == hash, ct);
        if (row is null || row.ExpiresAt <= now) return null;

        var consumed = await _db.OAuthAuthCodes.Where(x => x.CodeHash == hash).ExecuteDeleteAsync(ct);
        if (consumed == 0) return null;

        return new AuthCode(code, row.ClientId, row.RedirectUri, row.CodeChallenge,
            row.UserId, row.TenantId, row.Role, row.Scope, row.ExpiresAt);
    }

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(code)));
}
