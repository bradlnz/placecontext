namespace PlaceContext.Infrastructure.Persistence;

public sealed class WorkItemRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string? Detail { get; set; }
    public int Priority { get; set; }          // WorkItemPriority enum value (Low=0..High=2)
    public string Status { get; set; } = "Queued";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
