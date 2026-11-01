namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Immutable reservation key: retries and duplicate execution messages are charged once.</summary>
public sealed class BillableRunRow : ITenantOwned
{
    public Guid TenantId { get; set; }
    public string RunKey { get; set; } = "";
    public Guid ProjectId { get; set; }
    public string Kind { get; set; } = "";
    public DateTimeOffset ReservedAt { get; set; }
    public DateTimeOffset PeriodStart { get; set; }
}
