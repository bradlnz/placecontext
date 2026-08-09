using PlaceContext.Application.Dtos;

namespace PlaceContext.Identity.Access;

/// <summary>Identity-owned facade for role and member-permission administration.</summary>
public interface IIdentityAccessService
{
    Task<UserPermissionsView> GetUserPermissionsAsync(Guid userId, CancellationToken ct = default);
    Task<UserPermissionsView> SetUserPermissionOverrideAsync(
        Guid userId,
        string permission,
        bool? allowed,
        CancellationToken ct = default);
    Task<IReadOnlyList<RoleView>> ListRolesAsync(CancellationToken ct = default);
    Task<RoleView> CreateRoleAsync(
        string name,
        IReadOnlyList<string> permissions,
        CancellationToken ct = default);
    Task<RoleView> UpdateRolePermissionsAsync(
        Guid roleId,
        IReadOnlyList<string> permissions,
        CancellationToken ct = default);
    Task<bool> DeleteRoleAsync(Guid roleId, CancellationToken ct = default);
}
