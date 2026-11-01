using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
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
    public void Only_server_configured_price_ids_map_to_paid_allowances()
    {
        var options = new StripeRunBillingOptions
        {
            StarterPriceId = "price_starter",
            GrowthPriceId = "price_growth",
            BusinessPriceId = "price_business"
        };
        Assert.Equal("starter", options.PlanForPrice("price_starter"));
        Assert.Equal("growth", options.PlanForPrice("price_growth"));
        Assert.Equal("business", options.PlanForPrice("price_business"));
        Assert.Null(options.PlanForPrice("price_malicious"));
        Assert.Null(options.PriceFor("unknown"));
        Assert.Equal(100, RunPlans.Included("free"));
        Assert.Equal(1000, RunPlans.Included("starter"));
        Assert.Equal(10000, RunPlans.Included("growth"));
        Assert.Equal(50000, RunPlans.Included("business"));
    }

    [Fact]
    public void Billing_snapshot_counts_only_remaining_runs_and_expired_access_fails_closed()
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new RunAllowanceSnapshot("starter", "active", 1000, 999,
            now.AddDays(-3), now.AddDays(20));
        Assert.Equal(1, plan.Remaining);
        Assert.True(plan.CanRun);
        Assert.False((plan with { UsedRuns = 1000 }).CanRun);
        Assert.False((plan with { Status = "inactive" }).CanRun);
        Assert.False((plan with { PeriodEnd = now.AddDays(-1) }).CanRun);
    }
}
