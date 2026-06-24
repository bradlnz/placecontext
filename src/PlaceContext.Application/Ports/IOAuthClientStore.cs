using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Persistence of OAuth client registrations so they survive restarts (DCR clients are long-lived).</summary>
public interface IOAuthClientStore
{
    Task<OAuthClient> RegisterAsync(IReadOnlyList<string> redirectUris, string name, CancellationToken ct = default);
    Task<OAuthClient?> GetAsync(string clientId, CancellationToken ct = default);
}
