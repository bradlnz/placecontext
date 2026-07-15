using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Sets or clears one member's explicit permission override on top of their role default.
/// <paramref name="Allowed"/> = true grants a permission their role wouldn't otherwise have;
/// false revokes one it would; null removes the override entirely so the permission reverts to
/// inheriting the role default. Gated on <c>members.manage</c> — only someone who can manage members
/// can reshape another member's permissions.
/// </summary>
public sealed record SetUserPermissionOverrideCommand(Guid UserId, string Permission, bool? Allowed)
    : ICommand<UserPermissionsView>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => PlaceContext.Application.Ports.Permission.MembersManage;
}
