using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Persistence of OAuth client registrations so they survive restarts (DCR clients are long-lived).</summary>
public interface IOAuthClientStore
{
    Task<OAuthClient> RegisterAsync(IReadOnlyList<string> redirectUris, string name, CancellationToken ct = default);
    Task<OAuthClient?> GetAsync(string clientId, CancellationToken ct = default);
    /// <summary>Returns the client for <paramref name="clientId"/>, creating it (or adding the redirect URI)
    /// if missing — self-heals a public PKCE client whose registration was lost (e.g. a reset database).</summary>
    Task<OAuthClient> EnsureAsync(string clientId, string redirectUri, CancellationToken ct = default);
}
