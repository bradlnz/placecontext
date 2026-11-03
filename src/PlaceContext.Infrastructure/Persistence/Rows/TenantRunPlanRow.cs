namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Tenant subscription with unlimited usage. NextDispatchAt is a
/// distributed, row-locked managed start-pacing cursor.</summary>
public sealed class TenantRunPlanRow : ITenantOwned
{
    public Guid TenantId
    {
        get; set;
    }
    public string Plan { get; set; } = "free";
    public string Status { get; set; } = "active";
    public int IncludedRuns
    {
        get; set;
    } // Legacy: zero means unlimited.
    public long UsedRuns
    {
        get; set;
    }
    public DateTimeOffset PeriodStart
    {
        get; set;
    }
    public DateTimeOffset PeriodEnd
    {
        get; set;
    }
    public DateTimeOffset? NextDispatchAt
    {
        get; set;
    }
    public string? StripeCustomerId
    {
        get; set;
    }
    public string? StripeSubscriptionId
    {
        get; set;
    }
    public long LastStripeEventCreated
    {
        get; set;
    }
}
