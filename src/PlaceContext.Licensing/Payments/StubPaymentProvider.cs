namespace PlaceContext.Licensing.Payments;

/// <summary>
/// A placeholder payment provider: every known license is <see cref="SubscriptionStatus.Active"/> unless an
/// override says otherwise. The override map (license key → status) lets you exercise the past-due / cancelled
/// paths from configuration without wiring a real billing system. Replace this with a Stripe-backed provider
/// to gate activation on live subscription state.
/// </summary>
public sealed class StubPaymentProvider : IPaymentProvider
{
    private readonly IReadOnlyDictionary<string, SubscriptionStatus> _overrides;

    public StubPaymentProvider(IReadOnlyDictionary<string, SubscriptionStatus>? overrides = null)
        => _overrides = overrides is null
            ? new Dictionary<string, SubscriptionStatus>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, SubscriptionStatus>(overrides, StringComparer.OrdinalIgnoreCase);

    public SubscriptionStatus StatusFor(string licenseKey)
        => _overrides.TryGetValue(licenseKey, out var status) ? status : SubscriptionStatus.Active;
}
