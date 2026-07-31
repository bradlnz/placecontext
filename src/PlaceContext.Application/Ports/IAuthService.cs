using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Registration + credential validation for portal users. Operates within the current tenant.</summary>
public interface IAuthService
{
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    /// <summary>True once the organisation has at least one member — used to close self-registration after the owner.</summary>
    Task<bool> HasAnyMembersAsync(CancellationToken ct = default);
    /// <summary>Creates a member with the given role in the current tenant; returns null if the email is already taken.</summary>
    Task<AuthUser?> RegisterAsync(string email, string displayName, string password, UserRole role, CancellationToken ct = default);
    /// <summary>
    /// Returns the sole operator (Owner) for the current tenant, creating one on first use. Backs the
    /// token sign-in path: a self-hosted deployment has no registration, so the cluster operator who
    /// presents a valid portal token is signed in as this user.
    /// </summary>
    Task<AuthUser> GetOrCreateOperatorAsync(CancellationToken ct = default);

    /// <summary>
    /// True when this tenant has no Owner with a real, operator-chosen password — i.e. first run. The
    /// machine-provisioned row behind <see cref="GetOrCreateOperatorAsync"/> never counts (its password
    /// hash is an unusable random placeholder — see that method's remarks), so a workspace that has only
    /// ever been reached via the HMAC portal token still requires interactive setup before password login
    /// works. Drives the /setup vs /login redirect for unauthenticated portal requests.
    /// </summary>
    Task<bool> IsUnconfiguredAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates the tenant's first admin (Owner, real password) from the first-run setup form. Fails
    /// closed: returns null if the tenant is already configured (<see cref="IsUnconfiguredAsync"/> is
    /// false) or the email is already taken, so the setup endpoint can never be replayed into a second
    /// admin — the caller should redirect to /login instead of retrying.
    /// </summary>
    Task<AuthUser?> CreateFirstAdminAsync(string email, string displayName, string password, CancellationToken ct = default);

    /// <summary>
    /// Verifies email + password for the password-login page. Returns null on any mismatch — unknown
    /// email, wrong password, or a member with no real password set — so the caller can show one generic
    /// "invalid email or password" message without revealing which part was wrong (no user enumeration).
    /// </summary>
    Task<AuthUser?> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default);

    /// <summary>True when the given user has email 2FA enabled.</summary>
    Task<bool> IsTwoFactorEnabledAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Sends a short-lived, single-use code through the configured Postmark provider.</summary>
    Task<EmailTwoFactorChallenge> IssueTwoFactorCodeAsync(
        Guid userId, CancellationToken ct = default);

    /// <summary>Consumes an emailed setup code and enables 2FA.</summary>
    Task<bool> ConfirmTwoFactorSetupAsync(
        Guid userId, string code, CancellationToken ct = default);

    /// <summary>Consumes an emailed login code for a user who already has 2FA enabled.</summary>
    Task<bool> VerifyTwoFactorCodeAsync(
        Guid userId, string code, CancellationToken ct = default);

    /// <summary>Disables 2FA for the user after consuming a current emailed code.</summary>
    Task<bool> DisableTwoFactorAsync(Guid userId, string currentCode, CancellationToken ct = default);
}

public sealed record EmailTwoFactorChallenge(
    string MaskedEmail,
    DateTimeOffset ExpiresAt);
