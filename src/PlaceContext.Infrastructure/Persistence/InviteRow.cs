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
    public DateTimeOffset? AcceptedAt { get; set; }
}
