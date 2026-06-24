using System.Security.Cryptography;
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
    private readonly AppDbContext _db;
    public MembershipService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<MemberView>> ListMembersAsync(CancellationToken ct = default)
    {
        var rows = await _db.Users.AsNoTracking().OrderByDescending(u => u.Role).ThenBy(u => u.Email).ToListAsync(ct);
        return rows.Select(u => new MemberView(u.Id, u.Email, u.DisplayName, u.Role, u.CreatedAt)).ToList();
    }

    public async Task SetRoleAsync(Guid userId, UserRole role, CancellationToken ct = default)
    {
        var row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (row is null) return;
        row.Role = role.ToString();
        await _db.SaveChangesAsync(ct);
    }

    public async Task<InviteView> CreateInviteAsync(string email, UserRole role, CancellationToken ct = default)
    {
        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        await _db.Invites.AddAsync(new InviteRow
        {
            Id = Guid.NewGuid(),
            Email = email,
            Role = role.ToString(),
            Token = token,
            CreatedAt = DateTimeOffset.UtcNow,
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
            Role = invite.Role,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _db.Users.AddAsync(user, ct);
        invite.AcceptedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var role = Enum.TryParse<UserRole>(user.Role, out var r) ? r : UserRole.Member;
        return new AuthUser(user.Id, user.TenantId, user.Email, user.DisplayName, role);
    }

    private Task<InviteRow?> PendingAsync(string token, CancellationToken ct)
        => _db.Invites.FirstOrDefaultAsync(i => i.Token == token && i.AcceptedAt == null, ct);
}
