using PlaceContext.Agents.Contracts.Dtos;
using PlaceContext.Agents.Contracts.Queries;
using PlaceContext.Agents.Domain.Repositories;
using PlaceContext.Agents.Mappers;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.Agents.Handlers;

public sealed class GetAgentsWorkspaceHandler(IAgentsRepository repository)
    : IQueryHandler<GetAgentsWorkspaceQuery, AgentsWorkspaceView>
{
    public async Task<AgentsWorkspaceView> HandleAsync(GetAgentsWorkspaceQuery query, CancellationToken ct = default)
    {
        // A scoped EF context intentionally serializes these reads. The HTTP API exposes one
        // composed snapshot so the browser still avoids a request waterfall.
        var profiles = await repository.ListProfilesAsync(ct);
        var staff = await repository.ListStaffAsync(query.ProjectId, ct);
        var assignments = await repository.ListAssignmentsAsync(query.ProjectId, ct);
        var approvals = await repository.ListApprovalsAsync(query.ProjectId, ct);
        return new AgentsWorkspaceView(
            profiles.Select(AgentsViewMapper.ToView).ToArray(),
            staff.Select(AgentsViewMapper.ToView).ToArray(),
            assignments.Select(AgentsViewMapper.ToView).ToArray(),
            approvals.Select(AgentsViewMapper.ToView).ToArray());
    }
}
