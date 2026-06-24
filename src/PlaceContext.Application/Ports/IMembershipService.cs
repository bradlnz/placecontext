using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Ports;

/// <summary>Organisation membership: listing members, changing roles, and the invite flow. Tenant-scoped.</summary>
public interface IMembershipService
{
    Task<IReadOnlyList<MemberView>> ListMembersAsync(CancellationToken ct = default);
    Task SetRoleAsync(Guid userId, UserRole role, CancellationToken ct = default);

    /// <summary>The current role of a member by id within the tenant, or null if no such user exists
    /// (e.g. a stale/ghost token whose user was removed or predates a DB reseed).</summary>
    Task<string?> GetRoleAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Creates a single-use invite for an email at a role and returns its token.</summary>
    Task<InviteView> CreateInviteAsync(string email, UserRole role, CancellationToken ct = default);

    /// <summary>The email + role a pending invite token grants, or null if invalid/used.</summary>
    Task<InviteInfo?> GetInviteAsync(string token, CancellationToken ct = default);

    /// <summary>Accepts an invite — creates the member with the invited email+role and consumes the token; null if invalid.</summary>
    Task<AuthUser?> AcceptInviteAsync(string token, string displayName, string password, CancellationToken ct = default);
}
