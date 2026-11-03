namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Immutable reservation key: retries and duplicate execution messages are charged once.</summary>
public sealed class BillableRunRow : ITenantOwned
{
    public Guid TenantId
    {
        get; set;
    }
    public string RunKey { get; set; } = "";
    public Guid ProjectId
    {
        get; set;
    }
    public string Kind { get; set; } = "";
    public DateTimeOffset ReservedAt
    {
        get; set;
    }
    /// <summary>Persisted managed admission slot; duplicate starts must wait for this same time.</summary>
    public DateTimeOffset? DispatchAfter
    {
        get; set;
    }
    public DateTimeOffset PeriodStart
    {
        get; set;
    }
}
