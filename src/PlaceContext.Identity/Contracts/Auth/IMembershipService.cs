using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Ports;

/// <summary>Organisation membership: listing members, changing roles, and the invite flow. Tenant-scoped.</summary>
public interface IMembershipService
{
    Task<IReadOnlyList<MemberView>> ListMembersAsync(CancellationToken ct = default);

    /// <summary>Assigns a role by name (a <c>role_definitions</c> row — custom roles included — or a
    /// <see cref="UserRole"/> enum name). Refuses Owner, unknown names, demoting the default admin, and
    /// demoting an Owner.</summary>
    Task SetRoleAsync(Guid userId, string roleName, CancellationToken ct = default);

    /// <summary>One member by id within the tenant, or null if no such user exists.</summary>
    Task<MemberView?> GetMemberAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Whether the given member is the tenant's bootstrap default admin.</summary>
    Task<bool> IsDefaultAdminAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Removes a member and their permission overrides. Refuses to remove the default admin, any
    /// Owner, or the caller themselves. Pending invites are standalone tokens and are left alone.
    /// </summary>
    Task DeleteMemberAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The current role of a member by id within the tenant, or null if no such user exists
    /// (e.g. a stale/ghost token whose user was removed or predates a DB reseed).</summary>
    Task<string?> GetRoleAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Creates a single-use invite for an email at a role (by name — see
    /// <see cref="SetRoleAsync"/>) and returns its token.</summary>
    Task<InviteView> CreateInviteAsync(string email, string roleName, CancellationToken ct = default);

    /// <summary>The email + role a pending invite token grants, or null if invalid/used.</summary>
    Task<InviteInfo?> GetInviteAsync(string token, CancellationToken ct = default);

    /// <summary>Accepts an invite — creates the member with the invited email+role and consumes the token; null if invalid.</summary>
    Task<AuthUser?> AcceptInviteAsync(string token, string displayName, string password, CancellationToken ct = default);
}
