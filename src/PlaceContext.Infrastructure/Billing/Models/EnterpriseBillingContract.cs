namespace PlaceContext.Infrastructure.Billing;

/// <summary>Tenant-bound Enterprise contract for signed Stripe subscriptions.</summary>
public sealed class EnterpriseBillingContract
{
    public string PriceId { get; set; } = "";
    public int RunsPerPeriod { get; set; } = 1_000_000;
    public int AiRequestsPerPeriod { get; set; } = 500_000;
    public int MaxStartsPerMinute
    {
        get; set;
    }
    public int MaxConcurrentRuns
    {
        get; set;
    }
}
