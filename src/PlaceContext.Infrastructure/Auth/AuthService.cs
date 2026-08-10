using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PlaceContext.Application.Auth;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Comms;
using PlaceContext.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Auth;

/// <summary>
/// Registration + credential validation against the <c>users</c> table. All queries are tenant-scoped
/// by the DbContext's global query filter, so a user can only ever be created in / matched within the
/// current tenant. The new user's <c>TenantId</c> is stamped automatically on save.
/// </summary>
public sealed class AuthService : IAuthService
{
    // E.164-ish: optional leading plus, 7–15 digits.
    private static readonly Regex PhoneNumberPattern = new(
        @"^\+?[0-9]{7,15}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly AppDbContext _db;
    private readonly IClientCommunicationSender _communications;
    private readonly CommunicationProviderService _providers;

    // A fixed, precomputed hash verified against on every *unknown* login attempt, so the real PBKDF2
    // work happens whether or not the email exists — otherwise a "known email, wrong password" request
    // and an "unknown email" request would take measurably different time and leak account existence.
    private static readonly string DummyHashForTiming = PasswordHasher.Hash(Guid.NewGuid().ToString("N"));

    public AuthService(
        AppDbContext db,
        IClientCommunicationSender communications,
        CommunicationProviderService providers)
    {
        _db = db;
        _communications = communications;
        _providers = providers;
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
        => await _db.Users.AsNoTracking().AnyAsync(u => u.Email == Normalize(email), ct);

    public Task<bool> HasAnyMembersAsync(CancellationToken ct = default)
        => _db.Users.AsNoTracking().AnyAsync(ct);

    public async Task<AuthUser?> RegisterAsync(string email, string displayName, string password, UserRole role, CancellationToken ct = default)
    {
        email = Normalize(email);
        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Email == email, ct))
            return null;

        var row = new UserRow
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? email.Split('@')[0] : displayName.Trim(),
            PasswordHash = PasswordHasher.Hash(password),
            PasswordSet = true, // a human chose this password
            Role = role.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _db.Users.AddAsync(row, ct);
        await _db.SaveChangesAsync(ct); // TenantId stamped here
        return ToAuthUser(row);
    }

    public async Task<AuthUser> GetOrCreateOperatorAsync(CancellationToken ct = default)
    {
        // The first user in the tenant is the operator. AsNoTracking + oldest-first keeps this stable
        // even if invites later add more members; the operator is whoever the deployment was created for.
        var existing = await _db.Users.OrderBy(u => u.CreatedAt).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            // The machine-provisioned operator is the administrator of a local/self-hosted workspace.
            // Older databases created it before IsDefaultAdmin existed, so adopt that row on startup.
            if (existing.Email == "operator@localhost" && !existing.IsDefaultAdmin)
            {
                existing.IsDefaultAdmin = true;
                await _db.SaveChangesAsync(ct);
            }
            return ToAuthUser(existing);
        }

        var row = new UserRow
        {
            Id = Guid.NewGuid(),
            Email = "operator@localhost",
            DisplayName = "Operator",
            // This hash is unusable (a random secret nobody holds) and PasswordSet stays false — sign-in
            // for this row happens only via the HMAC portal token, never a password. Leaving PasswordSet
            // false means this row alone never satisfies IsUnconfiguredAsync: a workspace reached only
            // through the machine token still needs a real /setup before interactive password login works.
            PasswordHash = PasswordHasher.Hash(Guid.NewGuid().ToString("N")),
            Role = UserRole.Owner.ToString(),
            IsDefaultAdmin = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _db.Users.AddAsync(row, ct);
        await _db.SaveChangesAsync(ct); // TenantId stamped here
        return ToAuthUser(row);
    }

    public async Task<bool> IsUnconfiguredAsync(CancellationToken ct = default)
        => !await IsConfiguredQuery().AnyAsync(ct);

    public async Task<AuthUser?> CreateFirstAdminAsync(string email, string displayName, string password, CancellationToken ct = default)
    {
        // Fail closed: once any Owner has a real password, /setup is a dead end for good. The check then
        // insert below has a narrow race window between two concurrent /setup submissions on a brand-new
        // tenant; the unique (TenantId, Email) index still rejects a duplicate email, and this is a
        // one-time action a single human operator drives once, so that residual window is an accepted,
        // low-severity risk rather than reason to add transaction-isolation machinery here.
        if (await IsConfiguredQuery().AnyAsync(ct))
            return null;

        email = Normalize(email);
        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Email == email, ct))
            return null; // email already taken within this tenant

        var row = new UserRow
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? email.Split('@')[0] : displayName.Trim(),
            PasswordHash = PasswordHasher.Hash(password),
            PasswordSet = true,
            Role = UserRole.Owner.ToString(),
            IsDefaultAdmin = true, // the first real Owner is the tenant's bootstrap administrator
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _db.Users.AddAsync(row, ct);
        try
        {
            await _db.SaveChangesAsync(ct); // TenantId stamped here
        }
        catch (DbUpdateException)
        {
            return null; // lost a race to a concurrent /setup submission
        }
        return ToAuthUser(row);
    }

    public async Task<AuthUser?> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default)
    {
        email = Normalize(email);
        var row = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email && u.PasswordSet, ct);
        // Verify against the real hash when the row exists, else a fixed dummy hash — so a mismatch always
        // costs the same PBKDF2 work and can't be timed to reveal whether the email exists (see the
        // DummyHashForTiming field for why this needs to be a real, precomputed hash).
        var ok = PasswordHasher.Verify(password, row?.PasswordHash ?? DummyHashForTiming);
        return ok && row is not null ? ToAuthUser(row) : null;
    }

    // The tenant is "configured" once it has an Owner with a real (human-chosen) password. Shared by
    // IsUnconfiguredAsync and the CreateFirstAdminAsync guard so both read the exact same signal.
    private IQueryable<UserRow> IsConfiguredQuery()
        => _db.Users.AsNoTracking().Where(u => u.PasswordSet && u.Role == nameof(UserRole.Owner));

    private static AuthUser ToAuthUser(UserRow r) => new(
        r.Id, r.TenantId, r.Email, r.DisplayName,
        string.IsNullOrWhiteSpace(r.Role) ? nameof(UserRole.Member) : r.Role);

    private static string Normalize(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public async Task<bool> IsTwoFactorRequiredAsync(CancellationToken ct = default)
        => (await _providers.TwoFactorChannelsAsync(ct)).Count > 0;

    public async Task<bool> IsTwoFactorEnabledAsync(Guid userId, CancellationToken ct = default)
        => await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.TwoFactorEnabled, ct);

    public async Task<TwoFactorDeliveryInfo> GetTwoFactorDeliveryInfoAsync(
        Guid userId, string? channel = null, CancellationToken ct = default)
    {
        var row = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (row is null) throw new InvalidOperationException("User not found.");

        var flagged = await _providers.TwoFactorChannelsAsync(ct);
        var effective = ResolveChannel(row, flagged, channel);
        var needsPhone = effective == "sms" && string.IsNullOrWhiteSpace(row.PhoneNumber);
        var destination = effective == "sms"
            ? needsPhone ? "" : MaskPhone(row.PhoneNumber!)
            : MaskEmail(row.Email);
        return new TwoFactorDeliveryInfo(
            effective, destination, needsPhone,
            flagged.Contains("email"), flagged.Contains("sms"));
    }

    public async Task<TwoFactorSettingsInfo> GetTwoFactorSettingsAsync(
        Guid userId, CancellationToken ct = default)
    {
        var row = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (row is null) throw new InvalidOperationException("User not found.");

        var flagged = await _providers.TwoFactorChannelsAsync(ct);
        return new TwoFactorSettingsInfo(
            flagged.Count > 0,
            row.TwoFactorChannel,
            row.PhoneNumber,
            flagged.Contains("email"),
            flagged.Contains("sms"));
    }

    public async Task<TwoFactorChallenge> IssueTwoFactorCodeAsync(
        Guid userId, string? channel = null, CancellationToken ct = default)
    {
        var row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (row is null) throw new InvalidOperationException("User not found.");

        var now = DateTimeOffset.UtcNow;
        if (row.TwoFactorCodeHash is not null
            && row.TwoFactorCodeLastSentAt is { } sentAt
            && sentAt > now.AddMinutes(-1))
            throw new InvalidOperationException("Wait one minute before requesting another code.");

        var flagged = await _providers.TwoFactorChannelsAsync(ct);
        var effective = ResolveChannel(row, flagged, channel);
        if (effective == "sms" && string.IsNullOrWhiteSpace(row.PhoneNumber))
            throw new InvalidOperationException(
                "Add a phone number to receive verification codes by SMS.");

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        row.TwoFactorCodeHash = PasswordHasher.Hash(code);
        row.TwoFactorCodeExpiresAt = now.AddMinutes(10);
        row.TwoFactorCodeLastSentAt = now;
        row.TwoFactorCodeFailedAttempts = 0;
        await _db.SaveChangesAsync(ct);

        var body = $"Your PlaceContext verification code is {code}.\n\n"
            + "This code expires in 10 minutes and can only be used once. "
            + "If you did not request it, you can ignore this message.";
        try
        {
            if (effective == "sms")
            {
                await _communications.SendAuthenticationSmsAsync(row.PhoneNumber!, body, ct);
            }
            else
            {
                await _communications.SendAuthenticationEmailAsync(
                    row.Email,
                    row.DisplayName,
                    "Your PlaceContext verification code",
                    body,
                    ct);
            }
        }
        catch
        {
            ClearChallenge(row);
            await _db.SaveChangesAsync(ct);
            throw;
        }

        var destination = effective == "sms" ? MaskPhone(row.PhoneNumber!) : MaskEmail(row.Email);
        return new TwoFactorChallenge(effective, destination, row.TwoFactorCodeExpiresAt.Value);
    }

    public async Task SetTwoFactorPhoneNumberAsync(
        Guid userId, string? phoneNumber, CancellationToken ct = default)
    {
        var row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (row is null) throw new InvalidOperationException("User not found.");

        var normalized = (phoneNumber ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            row.PhoneNumber = null;
            if (row.TwoFactorChannel == "sms") row.TwoFactorChannel = "email";
        }
        else
        {
            if (!PhoneNumberPattern.IsMatch(normalized))
                throw new ArgumentException(
                    "Enter a valid mobile number (7–15 digits, e.g. +15551234567).");
            row.PhoneNumber = normalized;
            row.TwoFactorChannel = "sms";
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetTwoFactorChannelAsync(
        Guid userId, string channel, CancellationToken ct = default)
    {
        channel = (channel ?? string.Empty).Trim().ToLowerInvariant();
        if (channel is not ("email" or "sms"))
            throw new ArgumentException("Channel must be 'email' or 'sms'.");

        var flagged = await _providers.TwoFactorChannelsAsync(ct);
        if (!flagged.Contains(channel))
            throw new InvalidOperationException(
                $"The {channel} channel is not available for verification codes.");

        var row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (row is null) throw new InvalidOperationException("User not found.");
        row.TwoFactorChannel = channel;
        await _db.SaveChangesAsync(ct);
    }

    // The user's preferred channel wins when it has a 2FA-flagged provider; otherwise whichever
    // channel is flagged (email preferred). With nothing flagged at all, email is the legacy default.
    private static string ResolveChannel(
        UserRow row, IReadOnlyCollection<string> flagged, string? requested)
    {
        if (requested is not null && flagged.Contains(requested)) return requested;
        if (flagged.Contains(row.TwoFactorChannel)) return row.TwoFactorChannel;
        if (flagged.Contains("email")) return "email";
        return flagged.FirstOrDefault() ?? "email";
    }

    public async Task<bool> ConfirmTwoFactorSetupAsync(
        Guid userId, string code, CancellationToken ct = default)
    {
        var row = await VerifyChallengeAsync(userId, code, requireEnabled: false, ct);
        if (row is null) return false;
        row.TwoFactorEnabled = true;
        row.TotpSecret = null;
        row.RecoveryCodesJson = null;
        ClearChallenge(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> VerifyTwoFactorCodeAsync(
        Guid userId, string code, CancellationToken ct = default)
    {
        // 2FA is org-wide mandatory now (driven by flagged providers, not the legacy per-user flag),
        // so login verification only checks the challenge itself.
        var row = await VerifyChallengeAsync(userId, code, requireEnabled: false, ct);
        if (row is null) return false;
        ClearChallenge(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DisableTwoFactorAsync(Guid userId, string currentCode, CancellationToken ct = default)
    {
        var row = await VerifyChallengeAsync(userId, currentCode, requireEnabled: true, ct);
        if (row is null) return false;

        row.TwoFactorEnabled = false;
        row.TotpSecret = null;
        row.RecoveryCodesJson = null;
        ClearChallenge(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<UserRow?> VerifyChallengeAsync(
        Guid userId,
        string code,
        bool requireEnabled,
        CancellationToken ct)
    {
        var row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (row is null || requireEnabled && !row.TwoFactorEnabled
            || string.IsNullOrWhiteSpace(row.TwoFactorCodeHash)
            || row.TwoFactorCodeExpiresAt is null)
            return null;

        if (row.TwoFactorCodeExpiresAt <= DateTimeOffset.UtcNow)
        {
            ClearChallenge(row);
            await _db.SaveChangesAsync(ct);
            return null;
        }

        var normalized = (code ?? string.Empty).Trim();
        if (normalized.Length != 6 || !normalized.All(char.IsDigit)
            || !PasswordHasher.Verify(normalized, row.TwoFactorCodeHash))
        {
            row.TwoFactorCodeFailedAttempts++;
            if (row.TwoFactorCodeFailedAttempts >= 5) ClearChallenge(row);
            await _db.SaveChangesAsync(ct);
            return null;
        }

        return row;
    }

    private static void ClearChallenge(UserRow row)
    {
        row.TwoFactorCodeHash = null;
        row.TwoFactorCodeExpiresAt = null;
        row.TwoFactorCodeFailedAttempts = 0;
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return email;
        var local = email[..at];
        var visible = local.Length <= 2 ? local[..1] : local[..2];
        return visible + new string('•', Math.Max(2, local.Length - visible.Length)) + email[at..];
    }

    private static string MaskPhone(string phone)
    {
        var digits = phone.Length >= 2 ? phone[^2..] : phone;
        return "••••" + digits;
    }
}
