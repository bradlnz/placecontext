namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// The validated state of a self-host activation key: its <see cref="Status"/> plus the claims carried
/// by a valid key (the licensed organisation, plan tier, and expiry). Industry-agnostic licensing metadata.
/// </summary>
public sealed record ActivationInfo(
    ActivationStatus Status,
    string? Organisation,
    string? Plan,
    DateTimeOffset? ExpiresAt,
    string Reason)
{
    /// <summary>True only for a valid, unexpired, correctly-signed key.</summary>
    public bool IsActive => Status == ActivationStatus.Active;

    public static ActivationInfo Missing() =>
        new(ActivationStatus.Missing, null, null, null, "No activation key configured.");

    public static ActivationInfo Invalid(string reason) =>
        new(ActivationStatus.Invalid, null, null, null, reason);

    public static ActivationInfo Expired(string? organisation, string? plan, DateTimeOffset expiresAt) =>
        new(ActivationStatus.Expired, organisation, plan, expiresAt,
            $"Activation key expired on {expiresAt:yyyy-MM-dd}.");

    public static ActivationInfo Active(string? organisation, string? plan, DateTimeOffset? expiresAt) =>
        new(ActivationStatus.Active, organisation, plan, expiresAt, "Activated.");
}
