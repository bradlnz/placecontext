using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Agents;

public static class AgentDefinitionMapper
{
    public static AgentDefinitionView ToView(AgentDefinition agent)
        => new(agent.Id, agent.ProjectId, agent.Kind, agent.Name, agent.Description,
            agent.Instructions, agent.TemplateKey, agent.Capabilities, agent.AllowedJobIds,
            agent.ParentAgentId, agent.Enabled, agent.UpdatedAt);
}
