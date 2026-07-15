using System.Security.Cryptography;
using PlaceContext.Application.Auth;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Auth;

/// <summary>
/// Organisation membership against the tenant-scoped <c>users</c> and <c>invites</c> tables: listing
/// members, changing roles, and the single-use invite flow (an Admin invites an email at a role; the
/// invitee accepts to create their account with that role).
/// </summary>
public sealed class MembershipService : IMembershipService
{
    /// <summary>Invites expire after this period if not accepted.</summary>
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    private readonly AppDbContext _db;
    public MembershipService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<MemberView>> ListMembersAsync(CancellationToken ct = default)
    {
        var rows = await _db.Users.AsNoTracking().OrderByDescending(u => u.Role).ThenBy(u => u.Email).ToListAsync(ct);
        return rows.Select(u => new MemberView(u.Id, u.Email, u.DisplayName, u.Role, u.CreatedAt)).ToList();
    }

    public Task<string?> GetRoleAsync(Guid userId, CancellationToken ct = default)
        => _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => (string?)u.Role).FirstOrDefaultAsync(ct);

    public async Task SetRoleAsync(Guid userId, UserRole role, CancellationToken ct = default)
    {
        // Cap: cannot invite/promote to Owner via role dropdown — ownership transfer is separate.
        if (role == UserRole.Owner)
            throw new InvalidOperationException("Cannot assign the Owner role this way.");

        var row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (row is null) return;
        if (string.Equals(row.Role, nameof(UserRole.Owner), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot demote the Owner this way.");

        row.Role = role.ToString();
        await _db.SaveChangesAsync(ct);
    }

    public async Task<InviteView> CreateInviteAsync(string email, UserRole role, CancellationToken ct = default)
    {
        if (role == UserRole.Owner)
            throw new InvalidOperationException("Cannot invite someone as Owner.");

        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        await _db.Invites.AddAsync(new InviteRow
        {
            Id = Guid.NewGuid(),
            Email = email,
            Role = role.ToString(),
            Token = token,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(InviteLifetime),
        }, ct);
        await _db.SaveChangesAsync(ct);
        return new InviteView(email, role.ToString(), token);
    }

    public async Task<InviteInfo?> GetInviteAsync(string token, CancellationToken ct = default)
    {
        var row = await PendingAsync(token, ct);
        return row is null ? null : new InviteInfo(row.Email, row.Role);
    }

    public async Task<AuthUser?> AcceptInviteAsync(string token, string displayName, string password, CancellationToken ct = default)
    {
        var policyError = PasswordPolicy.Validate(password);
        if (policyError is not null)
            throw new ArgumentException(policyError);

        var invite = await PendingAsync(token, ct);
        if (invite is null) return null;
        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Email == invite.Email, ct))
            return null; // email already a member

        var user = new UserRow
        {
            Id = Guid.NewGuid(),
            Email = invite.Email,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? invite.Email.Split('@')[0] : displayName.Trim(),
            PasswordHash = PasswordHasher.Hash(password),
            PasswordSet = true, // a human chose this password
            Role = invite.Role == nameof(UserRole.Owner) ? nameof(UserRole.Member) : invite.Role,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _db.Users.AddAsync(user, ct);
        invite.AcceptedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var role = Enum.TryParse<UserRole>(user.Role, out var r) ? r : UserRole.Member;
        return new AuthUser(user.Id, user.TenantId, user.Email, user.DisplayName, role);
    }

    private Task<InviteRow?> PendingAsync(string token, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return _db.Invites.FirstOrDefaultAsync(
            i => i.Token == token
                 && i.AcceptedAt == null
                 && (i.ExpiresAt == null || i.ExpiresAt > now),
            ct);
    }
}
