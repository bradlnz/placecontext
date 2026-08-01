using System.Security.Cryptography;
using PlaceContext.Application.Auth;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Auth;

/// <summary>
/// Organisation membership against the tenant-scoped <c>users</c> and <c>invites</c> tables: listing
/// members, changing roles, and the single-use invite flow (an Admin invites an email at a role; the
/// invitee accepts to create their account with that role). Roles are handled as names — a
/// <c>role_definitions</c> row (custom roles included) or a <see cref="UserRole"/> enum name — and
/// stored verbatim on the user/invite rows.
/// </summary>
public sealed class MembershipService : IMembershipService
{
    /// <summary>Invites expire after this period if not accepted.</summary>
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IRoleDefinitionRepository _roles;
    public MembershipService(AppDbContext db, ICurrentUser currentUser, IRoleDefinitionRepository roles)
        => (_db, _currentUser, _roles) = (db, currentUser, roles);

    public async Task<IReadOnlyList<MemberView>> ListMembersAsync(CancellationToken ct = default)
    {
        var rows = await _db.Users.AsNoTracking().OrderByDescending(u => u.Role).ThenBy(u => u.Email).ToListAsync(ct);
        return rows.Select(u => new MemberView(u.Id, u.Email, u.DisplayName, u.Role, u.IsDefaultAdmin, u.CreatedAt)).ToList();
    }

    public async Task<MemberView?> GetMemberAsync(Guid userId, CancellationToken ct = default)
    {
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
        return u is null ? null : new MemberView(u.Id, u.Email, u.DisplayName, u.Role, u.IsDefaultAdmin, u.CreatedAt);
    }

    public async Task<bool> IsDefaultAdminAsync(Guid userId, CancellationToken ct = default)
        => await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.IsDefaultAdmin).FirstOrDefaultAsync(ct);

    public async Task DeleteMemberAsync(Guid userId, CancellationToken ct = default)
    {
        var row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (row is null) return;
        if (row.IsDefaultAdmin)
            throw new InvalidOperationException("The default admin cannot be removed.");
        if (string.Equals(row.Role, nameof(UserRole.Owner), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot remove the Owner this way.");
        if (userId == _currentUser.UserId)
            throw new InvalidOperationException("You cannot remove yourself.");

        // Overrides have no FK cascade — remove them explicitly before the user row. Pending invites
        // are standalone email tokens, not tied to the member row, so they are left alone.
        var grants = await _db.UserPermissionGrants.Where(g => g.UserId == userId).ToListAsync(ct);
        _db.UserPermissionGrants.RemoveRange(grants);
        _db.Users.Remove(row);
        await _db.SaveChangesAsync(ct);
    }

    public Task<string?> GetRoleAsync(Guid userId, CancellationToken ct = default)
        => _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => (string?)u.Role).FirstOrDefaultAsync(ct);

    public async Task SetRoleAsync(Guid userId, string roleName, CancellationToken ct = default)
    {
        var role = await ResolveAssignableRoleAsync(roleName, "Cannot assign the Owner role this way.", ct);

        var row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (row is null) return;
        if (row.IsDefaultAdmin)
            throw new InvalidOperationException("Cannot demote the default admin.");
        if (string.Equals(row.Role, nameof(UserRole.Owner), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot demote the Owner this way.");

        row.Role = role;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<InviteView> CreateInviteAsync(string email, string roleName, CancellationToken ct = default)
    {
        var role = await ResolveAssignableRoleAsync(roleName, "Cannot invite someone as Owner.", ct);

        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        await _db.Invites.AddAsync(new InviteRow
        {
            Id = Guid.NewGuid(),
            Email = email,
            Role = role,
            Token = token,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(InviteLifetime),
        }, ct);
        await _db.SaveChangesAsync(ct);
        return new InviteView(email, role, token);
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

        return new AuthUser(user.Id, user.TenantId, user.Email, user.DisplayName,
            string.IsNullOrWhiteSpace(user.Role) ? nameof(UserRole.Member) : user.Role);
    }

    /// <summary>
    /// Normalizes a caller-supplied role name to a known assignable role: a <c>role_definitions</c> row
    /// (custom roles included — the repository's read seeds the four system roles on first use), or a
    /// <see cref="UserRole"/> enum name as fallback. Owner stays unassignable — ownership transfer is a
    /// separate path — and anything else is rejected.
    /// </summary>
    private async Task<string> ResolveAssignableRoleAsync(string? roleName, string ownerError, CancellationToken ct)
    {
        var name = (roleName ?? string.Empty).Trim();
        if (string.Equals(name, nameof(UserRole.Owner), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(ownerError);

        var definition = await _roles.GetByNameAsync(name, ct);
        if (definition is not null)
            return definition.Name;
        if (Enum.TryParse<UserRole>(name, ignoreCase: true, out var parsed) && parsed != UserRole.Owner)
            return parsed.ToString();
        throw new ArgumentException($"Unknown role '{name}'.");
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
