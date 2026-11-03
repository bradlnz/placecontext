namespace PlaceContext.Application.AgentWorkflows;

public interface IAgentWorkflows
{
    Task<AgentWorkspace> LoadAsync(Guid projectId, CancellationToken ct = default);
    Task SetSubscriptionAsync(Guid projectId, string agentId, bool active, CancellationToken ct = default);
    Task<AgentRun> RunAsync(Guid projectId, string agentId, Guid requestId, string input, CancellationToken ct = default);
}
