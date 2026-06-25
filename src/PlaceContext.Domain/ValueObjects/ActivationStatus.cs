namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Result of validating a self-host activation key. <see cref="Active"/> means a valid, unexpired,
/// correctly-signed key was supplied; the others explain why the deployment is not activated.
/// </summary>
public enum ActivationStatus
{
    /// <summary>No activation key was configured.</summary>
    Missing,

    /// <summary>The key is malformed or its signature did not verify against the configured public key.</summary>
    Invalid,

    /// <summary>The key verified but its expiry date has passed.</summary>
    Expired,

    /// <summary>A valid, unexpired, correctly-signed key.</summary>
    Active
}
