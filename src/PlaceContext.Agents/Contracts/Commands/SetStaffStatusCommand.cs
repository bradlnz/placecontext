using PlaceContext.Agents.Contracts.Dtos;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Contracts.Commands;

public sealed record SetStaffStatusCommand(Guid Id, string Status)
    : ICommand<StaffMemberView?>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.AgentsManage;
}
