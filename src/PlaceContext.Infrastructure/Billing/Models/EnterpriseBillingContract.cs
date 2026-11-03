namespace PlaceContext.Infrastructure.Billing;

/// <summary>Tenant-bound Enterprise contract for signed Stripe subscriptions.</summary>
public sealed class EnterpriseBillingContract
{
    public string PriceId { get; set; } = "";
    public int MaxStartsPerMinute
    {
        get; set;
    }
    public int MaxConcurrentRuns
    {
        get; set;
    }
}
