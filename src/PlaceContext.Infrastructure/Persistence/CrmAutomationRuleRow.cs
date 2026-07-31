namespace PlaceContext.Infrastructure.Persistence;

public sealed class CrmAutomationRuleRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string EventType { get; set; } = "";
    public string? LifecycleStage { get; set; }
    public Guid ChainId { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
