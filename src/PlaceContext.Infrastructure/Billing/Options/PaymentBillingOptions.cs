using PlaceContext.Application.Billing;

namespace PlaceContext.Infrastructure.Billing;

public sealed class PaymentBillingOptions
{
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string BasicPriceId { get; set; } = "";
    public string StandardPriceId { get; set; } = "";
    public string EnterpriseSalesUrl { get; set; } = "";
    public Dictionary<string, EnterpriseBillingContract> EnterpriseContracts
    {
        get; set;
    } =
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
        if (tenantId == Guid.Empty)
        {
            return null;
        }
        return EnterpriseContracts.TryGetValue(tenantId.ToString("D"), out var contract)
                    && contract is { MaxStartsPerMinute: > 0, MaxConcurrentRuns: > 0, RunsPerPeriod: > 0, AiRequestsPerPeriod: > 0 }
                    && !string.IsNullOrWhiteSpace(contract.PriceId) ? contract : null;
    }

    public PaymentEntitlement? EntitlementForPrice(string? price, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(price) || tenantId == Guid.Empty)
        {
            return null;
        }
        if (ContractFor(tenantId) is { } contract && price == contract.PriceId)
        {
            return new PaymentEntitlement(RunPlans.Enterprise);
        }
        if (price == BasicPriceId && !string.IsNullOrWhiteSpace(BasicPriceId)
                    || price == StarterPriceId && !string.IsNullOrWhiteSpace(StarterPriceId))
        {
            return new PaymentEntitlement(RunPlans.Basic);
        }
        if (price == StandardPriceId && !string.IsNullOrWhiteSpace(StandardPriceId)
                    || price == GrowthPriceId && !string.IsNullOrWhiteSpace(GrowthPriceId))
        {
            return new PaymentEntitlement(RunPlans.Standard);
        }
        if (price == BusinessPriceId && !string.IsNullOrWhiteSpace(BusinessPriceId))
        {
            return new PaymentEntitlement("business");
        }
        return null;
    }

    public string? PlanForPrice(string? price, Guid tenantId)
        => EntitlementForPrice(price, tenantId)?.Plan;
}
