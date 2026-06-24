using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Persists OAuth client registrations so dynamically-registered clients survive restarts.</summary>
public sealed class EfOAuthClientStore : IOAuthClientStore
{
    private readonly AppDbContext _db;
    public EfOAuthClientStore(AppDbContext db) => _db = db;

    public async Task<OAuthClient> RegisterAsync(IReadOnlyList<string> redirectUris, string name, CancellationToken ct = default)
    {
        var row = new OAuthClientRow
        {
            ClientId = "pc_" + Guid.NewGuid().ToString("N"),
            RedirectUris = JsonSerializer.Serialize(redirectUris),
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

    public async Task<OAuthClient> EnsureAsync(string clientId, string redirectUri, CancellationToken ct = default)
    {
        var row = await _db.OAuthClients.FirstOrDefaultAsync(x => x.ClientId == clientId, ct);
        if (row is null)
        {
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

        var uris = JsonSerializer.Deserialize<List<string>>(row.RedirectUris) ?? new();
        if (!uris.Contains(redirectUri))
        {
            uris.Add(redirectUri);
            row.RedirectUris = JsonSerializer.Serialize(uris);
            await _db.SaveChangesAsync(ct);
        }
        return ToDomain(row);
    }

    private static OAuthClient ToDomain(OAuthClientRow r) => new(
        r.ClientId, JsonSerializer.Deserialize<List<string>>(r.RedirectUris) ?? new(), r.Name);
}
