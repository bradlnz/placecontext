namespace PlaceContext.Application.AgentWorkflows;

public sealed record AgentWorkspace(bool SimulationEnabled, bool CanManageSubscriptions,
    IReadOnlyList<AgentWorkflow> Workflows, IReadOnlyList<AgentSubscription> Subscriptions,
    IReadOnlyList<AgentRun> Runs);
