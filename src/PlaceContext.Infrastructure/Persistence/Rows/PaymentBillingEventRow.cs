namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Persisted webhook deduplication; event IDs are supplied by verified Stripe signatures.</summary>
public sealed class PaymentBillingEventRow
{
    public string EventId { get; set; } = "";
    public DateTimeOffset ProcessedAt
    {
        get; set;
    }
}
