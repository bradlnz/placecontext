namespace PlaceContext.Infrastructure.Persistence;

/// <summary>A refresh token grant, keyed by the SHA-256 hash of the token (the raw token is never
/// stored). Global (the row itself carries the tenant it renews access for).</summary>
public sealed class OAuthRefreshTokenRow
{
    public string TokenHash { get; set; } = "";
    public string ClientId { get; set; } = "";
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Role { get; set; } = "";
    public string Scope { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
