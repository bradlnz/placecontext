namespace PlaceContext.Infrastructure.Persistence;

/// <summary>A single-use invitation to join the organisation at a given role.</summary>
public sealed class InviteRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = "";
    public string Role { get; set; } = "Member";   // UserRole name
    public string Token { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>When set, the invite is invalid after this instant (null = legacy non-expiring rows).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}
