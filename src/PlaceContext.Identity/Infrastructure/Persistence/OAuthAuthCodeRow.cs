namespace PlaceContext.Identity.Infrastructure.Persistence;

/// <summary>A short-lived authorization code bound to a user, tenant, role, client, and PKCE challenge.
/// Keyed by the SHA-256 hash of the code (the raw code is never stored). Global (the row itself
/// carries the tenant it authorizes access for).</summary>
public sealed class OAuthAuthCodeRow
{
    public string CodeHash { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string CodeChallenge { get; set; } = "";
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Role { get; set; } = "";
    public string Scope { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}
