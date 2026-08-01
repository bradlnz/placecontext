using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>
/// Deletes a custom role. System roles cannot be deleted, nor can a role still assigned to any member.
/// Gated on <c>members.manage</c>.
/// </summary>
public sealed record DeleteRoleCommand(Guid RoleId) : ICommand<bool>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => PlaceContext.Application.Ports.Permission.MembersManage;
}
