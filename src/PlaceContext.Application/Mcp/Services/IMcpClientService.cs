using System.Net.Http.Json;
using System.Text.Json;

namespace PlaceContext.Application.Mcp;

/// <summary>
/// Service for interacting with external MCP (Model Context Protocol) servers.
/// Supports HTTP/SSE transports and tool discovery/invocation.
/// </summary>
public interface IMcpClientService
{
    /// <summary>
    /// Lists available tools from an MCP server.
    /// </summary>
    Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(Guid connectionId, CancellationToken ct = default);

    /// <summary>
    /// Calls a tool on an MCP server.
    /// </summary>
    Task<McpToolResult> CallToolAsync(Guid connectionId, string toolName, JsonElement? arguments = null, CancellationToken ct = default);

    /// <summary>
    /// Lists all configured MCP connections for a project.
    /// </summary>
    Task<IReadOnlyList<McpConnectionInfo>> ListConnectionsAsync(Guid projectId, CancellationToken ct = default);
}
