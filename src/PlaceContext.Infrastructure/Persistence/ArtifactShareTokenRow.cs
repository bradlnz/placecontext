namespace PlaceContext.Infrastructure.Persistence;

/// <summary>
/// One active-or-revoked public share credential per run artifact. TokenHash is the only persisted
/// representation of the bearer secret; TokenPrefix is non-sensitive UI identification only.
/// </summary>
public sealed class ArtifactShareTokenRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ArtifactId { get; set; }
    public string TokenHash { get; set; } = "";
    public string TokenPrefix { get; set; } = "";
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastAccessedAt { get; set; }
}
