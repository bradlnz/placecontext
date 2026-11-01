namespace PlaceContext.Application.Billing;

public interface IRunBillingPortal
{
    Task<Uri> CheckoutAsync(string plan, Uri returnBase, CancellationToken ct = default);
    Task<Uri> PortalAsync(Uri returnBase, CancellationToken ct = default);
}

public sealed class StripeRunBillingOptions
{
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string StarterPriceId { get; set; } = "";
    public string GrowthPriceId { get; set; } = "";
    public string BusinessPriceId { get; set; } = "";

    public string? PriceFor(string plan) => plan switch
    {
        "starter" => StarterPriceId,
        "growth" => GrowthPriceId,
        "business" => BusinessPriceId,
        _ => null
    };
    public string? PlanForPrice(string price) =>
        price == StarterPriceId && StarterPriceId.Length > 0 ? "starter" :
        price == GrowthPriceId && GrowthPriceId.Length > 0 ? "growth" :
        price == BusinessPriceId && BusinessPriceId.Length > 0 ? "business" : null;
}
