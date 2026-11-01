using System.Security.Cryptography;
using System.Text;
using PlaceContext.Application.Billing;
using PlaceContext.Infrastructure.Billing;

namespace PlaceContext.Infrastructure.Tests.Billing;

public sealed class StripeBillingSecurityTests
{
    [Fact]
    public void Webhook_requires_a_recent_valid_hmac_and_rejects_tampering()
    {
        const string body = "{\"id\":\"evt_test\",\"type\":\"customer.subscription.updated\"}";
        const string secret = "whsec_test_integration_secret";
        var now = DateTimeOffset.UtcNow;
        var stamp = now.ToUnixTimeSeconds().ToString();
        var signed = stamp + "." + body;
        var signature = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signed)))
            .ToLowerInvariant();
        var header = $"t={stamp},v1={signature}";
        Assert.True(StripeRunBillingWebhook.Verify(body, header, secret, now));
        Assert.False(StripeRunBillingWebhook.Verify(body + " ", header, secret, now));
        Assert.False(StripeRunBillingWebhook.Verify(body, header, "whsec_different", now));
        Assert.False(StripeRunBillingWebhook.Verify(body, "t=0,v1=" + signature, secret, now));
        Assert.False(StripeRunBillingWebhook.Verify(body, header, secret, now.AddMinutes(10)));
        Assert.False(StripeRunBillingWebhook.Verify(body, "t=notanumber,v1=x", secret, now));
    }

    [Fact]
    public void Only_server_configured_prices_map_to_self_service_entitlements()
    {
        var tenant = Guid.NewGuid();
        var options = new StripeRunBillingOptions
        {
            BasicPriceId = "price_basic",
            StandardPriceId = "price_standard",
            StarterPriceId = "price_legacy_starter",
            GrowthPriceId = "price_legacy_growth",
            BusinessPriceId = "price_legacy_business"
        };
        Assert.Equal("price_basic", options.PriceFor(RunPlans.Basic));
        Assert.Equal("price_standard", options.PriceFor(RunPlans.Standard));
        Assert.Null(options.PriceFor(RunPlans.Developer));
        Assert.Null(options.PriceFor(RunPlans.Enterprise));
        Assert.Null(options.PriceFor("arbitrary"));
        Assert.Equal(RunPlans.Basic, options.PlanForPrice("price_basic", tenant));
        Assert.Equal(RunPlans.Standard, options.PlanForPrice("price_standard", tenant));
        Assert.Equal(RunPlans.Basic, options.PlanForPrice("price_legacy_starter", tenant));
        Assert.Equal(RunPlans.Standard, options.PlanForPrice("price_legacy_growth", tenant));
        Assert.Equal("business", options.PlanForPrice("price_legacy_business", tenant));
        Assert.Null(options.PlanForPrice("price_malicious", tenant));
        Assert.Null(options.PlanForPrice("price_basic", Guid.Empty));
        Assert.Equal(0, RunPlans.Included(RunPlans.Developer));
        Assert.Equal(0, RunPlans.Included(RunPlans.Basic));
        Assert.Equal(0, RunPlans.Included(RunPlans.Standard));
        Assert.Equal(0, RunPlans.Included(RunPlans.Enterprise));
        Assert.Equal(0, RunPlans.StartsPerMinute(RunPlans.Developer));
        Assert.Equal(60, RunPlans.StartsPerMinute(RunPlans.Basic));
        Assert.Equal(600, RunPlans.StartsPerMinute(RunPlans.Standard));
    }

    [Fact]
    public void Enterprise_contract_price_and_throughput_are_tenant_specific_and_not_in_checkout()
    {
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var options = new StripeRunBillingOptions
        {
            EnterpriseContracts = new Dictionary<string, EnterpriseBillingContract>
            {
                [tenant.ToString("D")] = new() { PriceId = "price_contract_annual", MaxStartsPerMinute = 1500 }
            }
        };
        var entitlement = options.EntitlementForPrice("price_contract_annual", tenant);
        Assert.NotNull(entitlement);
        Assert.Equal(RunPlans.Enterprise, entitlement!.Plan);
        Assert.Equal(1500, options.ContractFor(tenant)!.MaxStartsPerMinute);
        Assert.Null(options.EntitlementForPrice("price_contract_annual", otherTenant));
        Assert.Null(options.PriceFor(RunPlans.Enterprise));

        options.EnterpriseContracts[tenant.ToString("D")].MaxStartsPerMinute = 0;
        Assert.Null(options.EntitlementForPrice("price_contract_annual", tenant));
    }

    [Fact]
    public void Tier_catalog_marks_developer_nonproduction_and_enterprise_quote_only()
    {
        Assert.Equal(new[] { RunPlans.Developer, RunPlans.Basic, RunPlans.Standard, RunPlans.Enterprise },
            RunPlans.Tiers.Select(t => t.Code));
        Assert.False(RunPlans.Tiers.Single(t => t.Code == RunPlans.Developer).SelfService);
        Assert.Null(RunPlans.Tiers.Single(t => t.Code == RunPlans.Enterprise).StartsPerMinute);
        Assert.Equal("Developer Edition", RunPlans.NameFor(RunPlans.Developer));
        Assert.Equal("Basic Managed (legacy)", RunPlans.NameFor("starter"));
    }

    [Fact]
    public void Billing_snapshot_has_unlimited_runs_and_expired_access_fails_closed()
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new RunAllowanceSnapshot(RunPlans.Basic, "active", long.MaxValue,
            now.AddDays(-3), now.AddDays(20), 60);
        Assert.True(plan.UnlimitedRuns);
        Assert.True(plan.CanRun);
        Assert.True((plan with { UsedRuns = long.MaxValue }).CanRun);
        Assert.False((plan with { Status = "inactive" }).CanRun);
        Assert.False((plan with { Status = "self_hosted_only" }).CanRun);
        Assert.False((plan with { PeriodStart = now.AddMinutes(1) }).CanRun);
        Assert.False((plan with { PeriodStart = now.AddMinutes(1) }).CanRun);
        Assert.False((plan with { PeriodEnd = now.AddDays(-1) }).CanRun);
    }
}
