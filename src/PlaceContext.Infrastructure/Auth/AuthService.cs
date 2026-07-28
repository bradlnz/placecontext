using System.Text.Json;
using PlaceContext.Application.Auth;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OtpNet;

namespace PlaceContext.Infrastructure.Auth;

/// <summary>
/// Registration + credential validation against the <c>users</c> table. All queries are tenant-scoped
/// by the DbContext's global query filter, so a user can only ever be created in / matched within the
/// current tenant. The new user's <c>TenantId</c> is stamped automatically on save.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IDataEncryptor _encryptor;

    // A fixed, precomputed hash verified against on every *unknown* login attempt, so the real PBKDF2
    // work happens whether or not the email exists — otherwise a "known email, wrong password" request
    // and an "unknown email" request would take measurably different time and leak account existence.
    private static readonly string DummyHashForTiming = PasswordHasher.Hash(Guid.NewGuid().ToString("N"));

    public AuthService(AppDbContext db, ICurrentTenant tenant, IDataEncryptor encryptor)
    {
        _db = db;
        _tenant = tenant;
        _encryptor = encryptor;
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
        var existing = await _db.Users.AsNoTracking().OrderBy(u => u.CreatedAt).FirstOrDefaultAsync(ct);
        if (existing is not null)
            return ToAuthUser(existing);

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
        Enum.TryParse<UserRole>(r.Role, out var role) ? role : UserRole.Member);

    private static string Normalize(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public async Task<bool> IsTwoFactorEnabledAsync(Guid userId, CancellationToken ct = default)
        => await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.TwoFactorEnabled, ct);

    public async Task<(string Secret, string[] RecoveryCodes)> SetupTwoFactorAsync(Guid userId, CancellationToken ct = default)
    {
        var row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (row is null) return ("", Array.Empty<string>());

        var secret = new byte[20];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            rng.GetBytes(secret);
        var secretBase32 = Base32Encoding.ToString(secret);

        var recoveryCodes = Enumerable.Range(0, 10)
            .Select(_ => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant())
            .ToArray();

        row.TotpSecret = _encryptor.Protect(secretBase32, IDataEncryptor.Purpose.Totp);
        row.RecoveryCodesJson = _encryptor.Protect(
            JsonSerializer.Serialize(recoveryCodes), IDataEncryptor.Purpose.Totp);
        row.TwoFactorEnabled = true;
        await _db.SaveChangesAsync(ct);

        return (secretBase32, recoveryCodes);
    }

    public async Task<bool> VerifyTotpCodeAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var row = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.TwoFactorEnabled, ct);
        if (row?.TotpSecret is null) return false;

        var secretBase32 = _encryptor.Unprotect(row.TotpSecret, IDataEncryptor.Purpose.Totp);
        if (string.IsNullOrEmpty(secretBase32)) return false;

        var secretBytes = Base32Encoding.ToBytes(secretBase32);
        var totp = new Totp(secretBytes);
        return totp.VerifyTotp(code.Trim(), out _, new VerificationWindow(1, 1));
    }

    public async Task<bool> ConsumeRecoveryCodeAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TwoFactorEnabled, ct);
        if (row?.RecoveryCodesJson is null) return false;

        var json = _encryptor.Unprotect(row.RecoveryCodesJson, IDataEncryptor.Purpose.Totp);
        if (string.IsNullOrEmpty(json)) return false;

        var codes = JsonSerializer.Deserialize<string[]>(json);
        if (codes is null) return false;

        var normalized = code.Trim().ToUpperInvariant();
        var idx = Array.FindIndex(codes, c => c == normalized);
        if (idx < 0) return false;

        codes[idx] = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(); // consume and rotate
        row.RecoveryCodesJson = _encryptor.Protect(
            JsonSerializer.Serialize(codes), IDataEncryptor.Purpose.Totp);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DisableTwoFactorAsync(Guid userId, string currentCode, CancellationToken ct = default)
    {
        if (!await VerifyTotpCodeAsync(userId, currentCode, ct)) return false;

        var row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (row is null) return false;

        row.TwoFactorEnabled = false;
        row.TotpSecret = null;
        row.RecoveryCodesJson = null;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
