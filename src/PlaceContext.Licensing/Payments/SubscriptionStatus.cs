namespace PlaceContext.Licensing.Payments;

/// <summary>The billing state of a license's subscription, as reported by the payment provider.</summary>
public enum SubscriptionStatus
{
    /// <summary>Paid and current — activation is granted.</summary>
    Active,

    /// <summary>Payment is overdue — activation is declined with 402 until it clears.</summary>
    PastDue,

    /// <summary>The subscription was cancelled — activation is declined with 403.</summary>
    Canceled
}
