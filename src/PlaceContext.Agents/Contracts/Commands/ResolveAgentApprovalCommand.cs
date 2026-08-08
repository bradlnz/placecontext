using PlaceContext.Agents.Contracts.Dtos;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Contracts.Commands;

public sealed record ResolveAgentApprovalCommand(Guid Id, string Decision, string? Comment = null)
    : ICommand<AgentApprovalView?>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.AgentsManage;
}
