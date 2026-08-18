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
    /// Resolves an existing tenant member by email or provisions a passwordless member for a trusted
    /// external identity. Existing roles are always retained; newly provisioned identities receive
    /// the supplied least-privilege role.
    /// </summary>
    Task<AuthUser> GetOrCreateExternalUserAsync(
        string email, string displayName, UserRole newUserRole, CancellationToken ct = default);
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

    /// <summary>
    /// True when 2FA is mandatory for the whole organisation — i.e. at least one enabled communication
    /// provider is flagged <c>UseForTwoFactor</c>. Supersedes the legacy per-user opt-in
    /// (<see cref="IsTwoFactorEnabledAsync"/>, kept for the legacy column only).
    /// </summary>
    Task<bool> IsTwoFactorRequiredAsync(CancellationToken ct = default);

    /// <summary>Legacy per-user 2FA opt-in flag — no longer consulted by the login flow.</summary>
    Task<bool> IsTwoFactorEnabledAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// How a verification code would reach this user right now: the effective channel (the user's
    /// preferred channel when flagged, else whichever channel is flagged — email preferred), the
    /// masked destination, and whether a phone number must be collected before a code can be sent.
    /// <paramref name="channel"/> overrides the effective channel (used by the channel-switch link).
    /// </summary>
    Task<TwoFactorDeliveryInfo> GetTwoFactorDeliveryInfoAsync(
        Guid userId, string? channel = null, CancellationToken ct = default);

    /// <summary>Org-wide 2FA status plus the user's own phone and preferred delivery channel.</summary>
    Task<TwoFactorSettingsInfo> GetTwoFactorSettingsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Sends a short-lived, single-use code through the effective channel (email → the account email;
    /// sms → <c>PhoneNumber</c>). <paramref name="channel"/> forces a specific flagged channel.
    /// Throws a friendly <see cref="InvalidOperationException"/> when the SMS channel has no phone
    /// number on file.
    /// </summary>
    Task<TwoFactorChallenge> IssueTwoFactorCodeAsync(
        Guid userId, string? channel = null, CancellationToken ct = default);

    /// <summary>
    /// Stores the user's mobile number (E.164) and switches their preferred channel to SMS — the
    /// enrollment step when SMS is the required channel and no number is on file. An empty value
    /// clears the number (and resets the channel to email).
    /// </summary>
    Task SetTwoFactorPhoneNumberAsync(Guid userId, string? phoneNumber, CancellationToken ct = default);

    /// <summary>Sets the user's preferred 2FA channel; the channel must have a flagged provider.</summary>
    Task SetTwoFactorChannelAsync(Guid userId, string channel, CancellationToken ct = default);

    /// <summary>Consumes an emailed setup code and enables 2FA (legacy opt-in flow).</summary>
    Task<bool> ConfirmTwoFactorSetupAsync(
        Guid userId, string code, CancellationToken ct = default);

    /// <summary>Consumes a login verification code.</summary>
    Task<bool> VerifyTwoFactorCodeAsync(
        Guid userId, string code, CancellationToken ct = default);

    /// <summary>Disables 2FA for the user after consuming a current emailed code (legacy opt-in flow).</summary>
    Task<bool> DisableTwoFactorAsync(Guid userId, string currentCode, CancellationToken ct = default);
}

/// <summary>A verification code dispatched to the user.</summary>
public sealed record TwoFactorChallenge(
    string Channel,
    string MaskedDestination,
    DateTimeOffset ExpiresAt);

/// <summary>Delivery routing for the login verify page (see <see cref="IAuthService.GetTwoFactorDeliveryInfoAsync"/>).</summary>
public sealed record TwoFactorDeliveryInfo(
    string Channel,
    string MaskedDestination,
    bool RequiresPhoneEnrollment,
    bool EmailAvailable,
    bool SmsAvailable);

/// <summary>Org-wide 2FA requirement plus the user's own delivery preferences.</summary>
public sealed record TwoFactorSettingsInfo(
    bool Required,
    string PreferredChannel,
    string? PhoneNumber,
    bool EmailAvailable,
    bool SmsAvailable);
