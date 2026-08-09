using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Replaces a role's permission grant set. Any role except Owner is editable (system roles included);
/// Owner always keeps the full catalog. Gated on <c>members.manage</c>.
/// </summary>
public sealed record UpdateRolePermissionsCommand(Guid RoleId, IReadOnlyList<string> Permissions)
    : ICommand<RoleView>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => PlaceContext.Application.Ports.Permission.MembersManage;
}
