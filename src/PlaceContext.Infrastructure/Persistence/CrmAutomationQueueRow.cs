namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Global durable queue; tenant context is restored by the CRM automation worker.</summary>
public sealed class CrmAutomationQueueRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RuleId { get; set; }
    public Guid ClientId { get; set; }
    public Guid ChainId { get; set; }
    public string EventType { get; set; } = "";
    public string LifecycleStage { get; set; } = "";
    public string RuleName { get; set; } = "";
    public DateTimeOffset EnqueuedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public string? ClaimedBy { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
}
