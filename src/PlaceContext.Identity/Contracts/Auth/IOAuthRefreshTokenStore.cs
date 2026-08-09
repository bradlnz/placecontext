namespace PlaceContext.Application.Ports;

/// <summary>
/// Persistence of OAuth refresh tokens so MCP clients renew access tokens without re-running the
/// browser flow — surviving server restarts. Tokens are single-use: every refresh rotates the token
/// (OAuth 2.1 public-client requirement), so a replayed token is dead on arrival.
/// </summary>
public interface IOAuthRefreshTokenStore
{
    /// <summary>Creates and persists a new refresh grant for the given identity.</summary>
    Task<OAuthRefreshGrant> IssueAsync(string clientId, Guid userId, Guid tenantId, string role, string scope,
        CancellationToken ct = default);

    /// <summary>Consumes the presented token and returns its replacement grant, or null when the token
    /// is unknown, expired, bound to a different client, or already used (rotation reuse).</summary>
    Task<OAuthRefreshGrant?> RotateAsync(string token, string clientId, CancellationToken ct = default);
}
