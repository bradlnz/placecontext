using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Creates a custom (non-system) role with an explicit permission grant set. The name must be unique
/// within the tenant and not collide with a system role; every permission must be in the
/// <c>Permission</c> catalog. Gated on <c>members.manage</c>, mirroring the member-override editor.
/// </summary>
public sealed record CreateRoleCommand(string Name, IReadOnlyList<string> Permissions)
    : ICommand<RoleView>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => PlaceContext.Application.Ports.Permission.MembersManage;
}
