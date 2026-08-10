using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Agents;

public sealed record CommandAgentRoute(
    AgentDefinition CommandAgent,
    IReadOnlyList<AgentDefinition> CollaboratingAgents,
    string PromptSection)
{
    public AgentDefinition ExecutingAgent => CollaboratingAgents.FirstOrDefault() ?? CommandAgent;
    public bool IsDelegated => CollaboratingAgents.Count > 0;

    public bool CanUse(string toolName, string args)
        => CollaboratingAgents.Count == 0
            ? AgentToolAuthorization.CanUse(CommandAgent, toolName, args)
            : CollaboratingAgents.Any(agent => AgentToolAuthorization.CanUse(agent, toolName, args));
}
