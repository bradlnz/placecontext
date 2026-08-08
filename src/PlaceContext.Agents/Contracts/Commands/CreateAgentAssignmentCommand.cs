using PlaceContext.Agents.Contracts.Dtos;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Contracts.Commands;

public sealed record CreateAgentAssignmentCommand(
    Guid StaffMemberId, Guid ProjectId, string Objective,
    DateTimeOffset? ScheduledFor = null, Guid? ParentAssignmentId = null,
    Guid? DelegatedByStaffMemberId = null, Guid? ScheduleId = null)
    : ICommand<AgentAssignmentView>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.AgentsManage;
}
