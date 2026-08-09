using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;

namespace PlaceContext.Identity.Access;

/// <summary>Thin Identity facade over the service-owned access commands and queries.</summary>
public sealed class IdentityAccessService(IDispatcher dispatcher) : IIdentityAccessService
{
    public Task<UserPermissionsView> GetUserPermissionsAsync(Guid userId, CancellationToken ct = default)
        => dispatcher.Query(new GetUserPermissionsQuery(userId), ct);

    public Task<UserPermissionsView> SetUserPermissionOverrideAsync(
        Guid userId,
        string permission,
        bool? allowed,
        CancellationToken ct = default)
        => dispatcher.Send(new SetUserPermissionOverrideCommand(userId, permission, allowed), ct);

    public Task<IReadOnlyList<RoleView>> ListRolesAsync(CancellationToken ct = default)
        => dispatcher.Query(new ListRolesQuery(), ct);

    public Task<RoleView> CreateRoleAsync(
        string name,
        IReadOnlyList<string> permissions,
        CancellationToken ct = default)
        => dispatcher.Send(new CreateRoleCommand(name, permissions), ct);

    public Task<RoleView> UpdateRolePermissionsAsync(
        Guid roleId,
        IReadOnlyList<string> permissions,
        CancellationToken ct = default)
        => dispatcher.Send(new UpdateRolePermissionsCommand(roleId, permissions), ct);

    public Task<bool> DeleteRoleAsync(Guid roleId, CancellationToken ct = default)
        => dispatcher.Send(new DeleteRoleCommand(roleId), ct);
}
