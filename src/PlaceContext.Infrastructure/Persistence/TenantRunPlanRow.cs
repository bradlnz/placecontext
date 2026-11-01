namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Billing-authoritative subscription entitlement; one row per tenant.</summary>
public sealed class TenantRunPlanRow : ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Plan { get; set; } = "free";
    public string Status { get; set; } = "active";
    public int IncludedRuns { get; set; } = 100;
    public int UsedRuns { get; set; }
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public long LastStripeEventCreated { get; set; }
}
