using System.Text.Json;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Mcp;

public interface IMcpClientService
{
    Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default);

    Task<McpToolResult> CallToolAsync(
        Guid connectionId,
        string toolName,
        JsonElement? arguments = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<McpConnectionInfo>> ListConnectionsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}
