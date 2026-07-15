using System.Text.Json;
using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Persists OAuth client registrations so dynamically-registered clients survive restarts.</summary>
public sealed class EfOAuthClientStore : IOAuthClientStore
{
    private readonly AppDbContext _db;
    public EfOAuthClientStore(AppDbContext db) => _db = db;

    public async Task<OAuthClient> RegisterAsync(IReadOnlyList<string> redirectUris, string name, CancellationToken ct = default)
    {
        var safe = redirectUris.Where(IsSafeRedirectUri).Distinct(StringComparer.Ordinal).ToList();
        if (safe.Count == 0)
            throw new ArgumentException("At least one valid redirect_uri is required (https or localhost/127.0.0.1).");

        var row = new OAuthClientRow
        {
            ClientId = "pc_" + Guid.NewGuid().ToString("N"),
            RedirectUris = JsonSerializer.Serialize(safe),
            Name = string.IsNullOrWhiteSpace(name) ? "MCP Client" : name,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _db.OAuthClients.AddAsync(row, ct);
        await _db.SaveChangesAsync(ct);
        return ToDomain(row);
    }

    public async Task<OAuthClient?> GetAsync(string clientId, CancellationToken ct = default)
    {
        var row = await _db.OAuthClients.AsNoTracking().FirstOrDefaultAsync(x => x.ClientId == clientId, ct);
        return row is null ? null : ToDomain(row);
    }

    /// <summary>
    /// Returns the registered client, or auto-registers a <em>loopback-only</em> public client when
    /// the client is unknown and the redirect is localhost/127.0.0.1 (typical MCP desktop clients
    /// after a DB reset). Never appends an arbitrary external redirect_uri to an existing client —
    /// that was an OAuth authorization-code theft hole.
    /// </summary>
    public async Task<OAuthClient> EnsureAsync(string clientId, string redirectUri, CancellationToken ct = default)
    {
        var row = await _db.OAuthClients.FirstOrDefaultAsync(x => x.ClientId == clientId, ct);
        if (row is not null)
            return ToDomain(row);

        // Self-heal only for loopback redirects — never for arbitrary attacker-controlled hosts.
        if (!IsLoopbackRedirectUri(redirectUri))
            throw new InvalidOperationException("Unknown OAuth client. Re-register via /connect/register.");

        row = new OAuthClientRow
        {
            ClientId = clientId,
            RedirectUris = JsonSerializer.Serialize(new[] { redirectUri }),
            Name = "MCP Client",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _db.OAuthClients.AddAsync(row, ct);
        await _db.SaveChangesAsync(ct);
        return ToDomain(row);
    }

    /// <summary>https (any host) or http://localhost / http://127.0.0.1 (with optional port).</summary>
    internal static bool IsSafeRedirectUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return false;
        if (u.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return true;
        return IsLoopbackRedirectUri(uri);
    }

    internal static bool IsLoopbackRedirectUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return false;
        if (!u.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !u.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;
        return u.IsLoopback
            || string.Equals(u.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(u.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private static OAuthClient ToDomain(OAuthClientRow r) => new(
        r.ClientId, JsonSerializer.Deserialize<List<string>>(r.RedirectUris) ?? new(), r.Name);
}
