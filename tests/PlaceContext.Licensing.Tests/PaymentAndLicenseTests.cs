using PlaceContext.Licensing.Licensing;
using PlaceContext.Licensing.Payments;

namespace PlaceContext.Licensing.Tests;

/// <summary>Tests the pluggable payment gate and the license catalogue used by the activation endpoint.</summary>
public class PaymentAndLicenseTests
{
    [Fact]
    public void Stub_payment_provider_defaults_to_active()
        => Assert.Equal(SubscriptionStatus.Active, new StubPaymentProvider().StatusFor("any-key"));

    [Fact]
    public void Stub_payment_provider_applies_overrides_case_insensitively()
    {
        var provider = new StubPaymentProvider(new Dictionary<string, SubscriptionStatus>
        {
            ["PAST-DUE-KEY"] = SubscriptionStatus.PastDue,
            ["CANCELLED-KEY"] = SubscriptionStatus.Canceled,
        });

        Assert.Equal(SubscriptionStatus.PastDue, provider.StatusFor("past-due-key"));
        Assert.Equal(SubscriptionStatus.Canceled, provider.StatusFor("cancelled-key"));
        Assert.Equal(SubscriptionStatus.Active, provider.StatusFor("unknown"));
    }

    [Fact]
    public void License_store_finds_known_keys_case_insensitively_and_misses_unknown()
    {
        var store = new LicenseStore(new[] { new License("DEMO-PLACECONTEXT", "Demo Org", "pro") });

        var found = store.Find("demo-placecontext");
        Assert.NotNull(found);
        Assert.Equal("Demo Org", found!.Org);
        Assert.Null(store.Find("nope"));
    }
}
