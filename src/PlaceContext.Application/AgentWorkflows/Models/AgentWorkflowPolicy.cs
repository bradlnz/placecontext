namespace PlaceContext.Application.AgentWorkflows;

public sealed class AgentWorkflowPolicy(Func<bool> simulationEnabled)
{
    public bool SimulationEnabled => simulationEnabled();
}
