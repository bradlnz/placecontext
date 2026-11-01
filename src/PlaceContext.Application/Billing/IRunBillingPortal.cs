namespace PlaceContext.Application.Billing;

public interface IRunBillingPortal
{
    Task<Uri> CheckoutAsync(string plan, Uri returnBase, CancellationToken ct = default);
    Task<Uri> PortalAsync(Uri returnBase, CancellationToken ct = default);
}

/// <summary>Host-only setting: Developer Edition is self-hosted, not an unlimited
/// free sandbox on the paid public SaaS instance.</summary>
public sealed class RunBillingDeploymentOptions
{
    public bool ManagedHosting { get; set; }
    public bool RequireExplicitTenantProvisioning { get; set; }
}

/// <summary>Tenant-bound Enterprise contract for signed Stripe subscriptions.</summary>
public sealed class EnterpriseBillingContract
{
    public string PriceId { get; set; } = "";
    public int MaxStartsPerMinute { get; set; }
    public int MaxConcurrentRuns { get; set; }
}

public sealed record StripeRunEntitlement(string Plan);

public sealed class StripeRunBillingOptions
{
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string BasicPriceId { get; set; } = "";
    public string StandardPriceId { get; set; } = "";
    public string EnterpriseSalesUrl { get; set; } = "";
    public Dictionary<string, EnterpriseBillingContract> EnterpriseContracts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    // Legacy prices remain valid for pre-existing subscriptions but never for new Checkout.
    public string StarterPriceId { get; set; } = "";
    public string GrowthPriceId { get; set; } = "";
    public string BusinessPriceId { get; set; } = "";

    public string? PriceFor(string plan) => plan switch
    {
        RunPlans.Basic when !string.IsNullOrWhiteSpace(BasicPriceId) => BasicPriceId,
        RunPlans.Standard when !string.IsNullOrWhiteSpace(StandardPriceId) => StandardPriceId,
        _ => null
    };

    public EnterpriseBillingContract? ContractFor(Guid tenantId)
    {
        if (tenantId == Guid.Empty) return null;
        return EnterpriseContracts.TryGetValue(tenantId.ToString("D"), out var contract)
            && contract is { MaxStartsPerMinute: > 0, MaxConcurrentRuns: > 0 }
            && !string.IsNullOrWhiteSpace(contract.PriceId) ? contract : null;
    }

    public StripeRunEntitlement? EntitlementForPrice(string? price, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(price) || tenantId == Guid.Empty) return null;
        if (ContractFor(tenantId) is { } contract && price == contract.PriceId)
            return new StripeRunEntitlement(RunPlans.Enterprise);
        if (price == BasicPriceId && !string.IsNullOrWhiteSpace(BasicPriceId)
            || price == StarterPriceId && !string.IsNullOrWhiteSpace(StarterPriceId))
            return new StripeRunEntitlement(RunPlans.Basic);
        if (price == StandardPriceId && !string.IsNullOrWhiteSpace(StandardPriceId)
            || price == GrowthPriceId && !string.IsNullOrWhiteSpace(GrowthPriceId))
            return new StripeRunEntitlement(RunPlans.Standard);
        if (price == BusinessPriceId && !string.IsNullOrWhiteSpace(BusinessPriceId))
            return new StripeRunEntitlement("business");
        return null;
    }

    public string? PlanForPrice(string? price, Guid tenantId)
        => EntitlementForPrice(price, tenantId)?.Plan;
}
