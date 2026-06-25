namespace PlaceContext.Licensing.Payments;

/// <summary>
/// The seam between the licensing server and billing. Activation is granted only when a license's
/// subscription is <see cref="SubscriptionStatus.Active"/>. Swap <see cref="StubPaymentProvider"/> for a
/// Stripe-backed (or other) implementation to make activation track real subscription state.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>The current billing state for the given license key.</summary>
    SubscriptionStatus StatusFor(string licenseKey);
}
