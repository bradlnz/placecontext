using PlaceContext.Application.Agents;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListAgentDefinitionsHandler(IAgentDefinitionRepository repository)
    : IQueryHandler<ListAgentDefinitionsQuery, IReadOnlyList<AgentDefinitionView>>
{
    public async Task<IReadOnlyList<AgentDefinitionView>> HandleAsync(ListAgentDefinitionsQuery query, CancellationToken ct = default)
        => (await repository.ListForProjectAsync(query.ProjectId, ct))
            .OrderBy(agent => agent.Kind == AgentKind.Command ? 0 : 1)
            .ThenBy(agent => agent.Name, StringComparer.OrdinalIgnoreCase)
            .Select(AgentDefinitionMapper.ToView)
            .ToArray();
}
