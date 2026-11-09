namespace PlaceContext.Application.AgentWorkflows;
public interface IAgentModelGateway
{
    Task<AgentCompletion> CompleteMeteredAsync(Guid connectionId, string model, string instruction, string input, CancellationToken ct);
}
