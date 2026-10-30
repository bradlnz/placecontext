namespace PlaceContext.Infrastructure.Persistence;

public sealed class AgentSubscriptionRow : ITenantOwned
{
    public Guid TenantId { get; set; }
    public string AgentId { get; set; } = "";
    public bool Active { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
