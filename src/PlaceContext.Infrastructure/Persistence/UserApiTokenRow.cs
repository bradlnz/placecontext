namespace PlaceContext.Infrastructure.Persistence;

/// <summary>
/// A personal API token a member mints from Settings. Only the SHA-256 hash is stored; the raw token
/// is shown once at creation. Authenticates machine callers as that user against the entity data API.
/// </summary>
public sealed class UserApiTokenRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Human label (e.g. "CI", "local script").</summary>
    public string Name { get; set; } = "";
    /// <summary>Hex SHA-256 of the raw token (constant-time lookup via prefix + verify).</summary>
    public string TokenHash { get; set; } = "";
    /// <summary>First 8 hex chars of the raw token — displayed so the user can recognise which token is which.</summary>
    public string TokenPrefix { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
