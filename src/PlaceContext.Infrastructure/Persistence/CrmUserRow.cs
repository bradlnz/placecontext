namespace PlaceContext.Infrastructure.Persistence;

public sealed class CrmUserRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string? Name { get; set; }
    public string Email { get; set; } = "";
    public string? JoinCode { get; set; }
    public DateTimeOffset? JoinCodeExpiresAt { get; set; }
    public Guid? AuthUserId { get; set; }
    public DateTimeOffset? OnboardedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
