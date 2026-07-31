namespace PlaceContext.Infrastructure.Persistence;

public sealed class CrmJobRunRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ClientId { get; set; }
    public Guid JobId { get; set; }
    public Guid RunId { get; set; }
    public string LifecycleStage { get; set; } = "Lead";
    public DateTimeOffset StartedAt { get; set; }
}
