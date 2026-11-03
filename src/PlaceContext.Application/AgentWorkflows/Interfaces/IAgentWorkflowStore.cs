namespace PlaceContext.Application.AgentWorkflows;

public interface IAgentWorkflowStore
{
    Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken ct);
    Task<IReadOnlyList<AgentSubscription>> ListSubscriptionsAsync(CancellationToken ct);
    Task<IReadOnlyList<AgentWorkflowRun>> ListRunsAsync(Guid projectId, CancellationToken ct);
    Task SetSubscriptionAsync(string agentId, bool active, DateTimeOffset updatedAt, CancellationToken ct);
    Task<bool> IsSubscribedAsync(string agentId, CancellationToken ct);
    Task<AgentWorkflowRun?> GetRunAsync(Guid id, CancellationToken ct);
    Task<bool> TryCreateRunAsync(AgentWorkflowRun run, CancellationToken ct);
    Task SaveRunAsync(AgentWorkflowRun run, AgentWorkflowUsage? usage, CancellationToken ct);
}
