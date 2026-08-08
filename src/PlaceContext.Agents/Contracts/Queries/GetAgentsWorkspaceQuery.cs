using PlaceContext.Agents.Contracts.Dtos;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Contracts.Queries;

public sealed record GetAgentsWorkspaceQuery(Guid? ProjectId = null)
    : IQuery<AgentsWorkspaceView>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.AgentsManage;
}
