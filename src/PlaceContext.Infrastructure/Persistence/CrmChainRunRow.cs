namespace PlaceContext.Infrastructure.Persistence;

public sealed class CrmChainRunRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ClientId { get; set; }
    public Guid ChainId { get; set; }
    public Guid ChainRunId { get; set; }
    public string LifecycleStage { get; set; } = "Lead";
    public DateTimeOffset StartedAt { get; set; }
}
