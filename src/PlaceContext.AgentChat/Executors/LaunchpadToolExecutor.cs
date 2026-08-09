using PlaceContext.AgentChat.Integration;

namespace PlaceContext.Application.Agents.Services;

public class LaunchpadToolExecutor(IAgentChatWorkspaceClient workspace)
{
    public virtual async Task<string> ExecuteAsync(
        Guid projectId,
        string toolName,
        string args,
        CancellationToken ct)
    {
        try
        {
            return await workspace.ExecuteToolAsync(projectId, toolName, args, ct);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}
