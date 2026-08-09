using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Identity.Infrastructure.Persistence;

public sealed class UserRow : IIdentityTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    /// <summary>
    /// True only when a human chose this password (first-run /setup, invite acceptance, self-registration).
    /// The machine-provisioned "operator" row (see <c>AuthService.GetOrCreateOperatorAsync</c>) stores an
    /// unusable random hash and leaves this false, so it never counts as a configured admin — see
    /// <c>AuthService.IsUnconfiguredAsync</c>.
    /// </summary>
    public bool PasswordSet { get; set; }
    public string Role { get; set; } = "Member";   // UserRole name
    /// <summary>
    /// The tenant's bootstrap administrator — the first real (human-password) Owner, stamped by
    /// /setup (<c>AuthService.CreateFirstAdminAsync</c>) or by the AddDefaultAdminAndRoleDefinitions
    /// migration for existing installs. The default admin cannot be deleted, demoted, or have
    /// <c>settings.manage</c> revoked, and is the only member the /settings/* area (beyond the
    /// self-service API tokens page) is open to.
    /// </summary>
    public bool IsDefaultAdmin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Legacy TOTP secret retained for migration compatibility.</summary>
    public string? TotpSecret { get; set; }
    /// <summary>Legacy recovery codes retained for migration compatibility.</summary>
    public string? RecoveryCodesJson { get; set; }
    /// <summary>Legacy per-user 2FA opt-in — superseded by org-wide mandatory 2FA (a communication
    /// provider flagged <c>UseForTwoFactor</c>); no longer consulted by the login flow.</summary>
    public bool TwoFactorEnabled { get; set; }
    /// <summary>Mobile number (E.164, e.g. +15551234567) receiving SMS verification codes.</summary>
    public string? PhoneNumber { get; set; }
    /// <summary>Preferred 2FA delivery channel: "email" (default) | "sms". Honoured only when that
    /// channel has an enabled provider flagged <c>UseForTwoFactor</c>; otherwise the flagged channel wins.</summary>
    public string TwoFactorChannel { get; set; } = "email";
    public string? TwoFactorCodeHash { get; set; }
    public DateTimeOffset? TwoFactorCodeExpiresAt { get; set; }
    public DateTimeOffset? TwoFactorCodeLastSentAt { get; set; }
    public int TwoFactorCodeFailedAttempts { get; set; }
}
