namespace PlaceContext.Application.Billing;

public interface IRunBillingPortal
{
    Task<Uri> CheckoutAsync(string plan, Uri returnBase, CancellationToken ct = default);
    Task<Uri> PortalAsync(Uri returnBase, CancellationToken ct = default);
}

/// <summary>Operator-controlled enterprise contract: the signed Stripe subscription must use
/// the exact price and tenant association provisioned for this customer.</summary>
public sealed class EnterpriseBillingContract
{
    public string PriceId { get; set; } = "";
    public int IncludedRuns { get; set; }
}

public sealed record StripeRunEntitlement(string Plan, int IncludedRuns);

public sealed class StripeRunBillingOptions
{
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string BasicPriceId { get; set; } = "";
    public string StandardPriceId { get; set; } = "";
    public string EnterpriseSalesUrl { get; set; } = "";
    public Dictionary<string, EnterpriseBillingContract> EnterpriseContracts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    // Accept existing configured Stripe prices for subscriptions created before these tiers.
    // Never expose these legacy products in new Checkout.
    public string StarterPriceId { get; set; } = "";
    public string GrowthPriceId { get; set; } = "";
    public string BusinessPriceId { get; set; } = "";

    public string? PriceFor(string plan) => plan switch
    {
        RunPlans.Basic when !string.IsNullOrWhiteSpace(BasicPriceId) => BasicPriceId,
        RunPlans.Standard when !string.IsNullOrWhiteSpace(StandardPriceId) => StandardPriceId,
        _ => null // Developer is free; Enterprise requires a sales-led contract.
    };

    public StripeRunEntitlement? EntitlementForPrice(string? price, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(price) || tenantId == Guid.Empty) return null;
        if (EnterpriseContracts.TryGetValue(tenantId.ToString("D"), out var contract)
            && contract is { IncludedRuns: > 0 } && !string.IsNullOrWhiteSpace(contract.PriceId)
            && price == contract.PriceId)
            return new StripeRunEntitlement(RunPlans.Enterprise, contract.IncludedRuns);

        if (price == BasicPriceId && !string.IsNullOrWhiteSpace(BasicPriceId)
            || price == StarterPriceId && !string.IsNullOrWhiteSpace(StarterPriceId))
            return new StripeRunEntitlement(RunPlans.Basic, RunPlans.BasicRuns);
        if (price == StandardPriceId && !string.IsNullOrWhiteSpace(StandardPriceId)
            || price == GrowthPriceId && !string.IsNullOrWhiteSpace(GrowthPriceId))
            return new StripeRunEntitlement(RunPlans.Standard, RunPlans.StandardRuns);
        if (price == BusinessPriceId && !string.IsNullOrWhiteSpace(BusinessPriceId))
            return new StripeRunEntitlement("business", RunPlans.Included("business"));
        return null;
    }

    public string? PlanForPrice(string? price, Guid tenantId)
        => EntitlementForPrice(price, tenantId)?.Plan;
}
