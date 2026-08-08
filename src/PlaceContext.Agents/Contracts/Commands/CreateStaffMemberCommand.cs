using PlaceContext.Agents.Contracts.Dtos;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Contracts.Commands;

public sealed record CreateStaffMemberCommand(
    Guid ProfileId, string Name, IReadOnlyList<Guid> ProjectIds,
    string? InstructionsOverride = null, string? ModelOverride = null)
    : ICommand<StaffMemberView>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.AgentsManage;
}
