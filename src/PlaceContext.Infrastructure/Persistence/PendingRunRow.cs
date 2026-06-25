namespace PlaceContext.Infrastructure.Persistence;

/// <summary>
/// A durable, cross-tenant queue row for a job run requested by a trigger. NOT tenant-owned — it is a
/// system queue drained by the background scheduler, which re-establishes the ambient tenant from
/// <see cref="TenantId"/> before dispatching. Rows are claimed atomically (FOR UPDATE SKIP LOCKED) so
/// any replica can drain safely, and deleted once dispatched.
/// </summary>
public sealed class PendingRunRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid JobId { get; set; }
    public Guid TriggerId { get; set; }
    public string TriggerName { get; set; } = "";
    public string? Payload { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
    public string? ClaimedBy { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
}
