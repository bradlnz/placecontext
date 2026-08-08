using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class GetAgentConfigHandler : IQueryHandler<GetAgentConfigQuery, AgentConfigView>
{
    private readonly IAgentConfigRepository _configs;

    public GetAgentConfigHandler(IAgentConfigRepository configs) => _configs = configs;

    public async Task<AgentConfigView> HandleAsync(GetAgentConfigQuery query, CancellationToken ct = default)
    {
        var config = await _configs.GetByProjectIdAsync(query.ProjectId, ct);
        if (config is null)
            return AgentConfigViewMapper.Default(query.ProjectId);
        return AgentConfigViewMapper.ToView(config);
    }
}
