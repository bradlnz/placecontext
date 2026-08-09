using System.Text.Json;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Dtos
{
    /// <summary>Read model for an external MCP server connection.</summary>
    public sealed record McpConnectionView(
        Guid Id,
        Guid ProjectId,
        string Name,
        string Transport,
        string? EndpointUrl,
        string? Command,
        string? Args,
        string? AuthType,
        bool Enabled,
        string? LastStatus,
        DateTimeOffset? LastConnectedAt,
        DateTimeOffset CreatedAt,
        string? OAuthAccessToken = null,
        DateTimeOffset? OAuthTokenExpiresAt = null,
        string? OAuthClientId = null,
        string? OAuthScopes = null);
}

namespace PlaceContext.Application.Features
{
    using PlaceContext.Application.Dtos;

    public sealed record CreateMcpConnectionCommand(
        Guid ProjectId,
        string Name,
        string Transport,
        string? EndpointUrl = null,
        string? Command = null,
        string? Args = null,
        string? AuthType = null,
        string? AuthToken = null,
        string? AuthHeader = null,
        string? OAuthClientId = null,
        string? OAuthScopes = null) : ICommand<McpConnectionView>;

    public sealed record UpdateMcpConnectionCommand(
        Guid Id,
        string Name,
        string Transport,
        string? EndpointUrl = null,
        string? Command = null,
        string? Args = null,
        string? AuthType = null,
        string? AuthToken = null,
        string? AuthHeader = null,
        string? OAuthClientId = null,
        string? OAuthScopes = null) : ICommand<McpConnectionView>;

    public sealed record DeleteMcpConnectionCommand(Guid Id) : ICommand<bool>;

    public sealed record TestMcpConnectionCommand(Guid Id) : ICommand<McpConnectionView>;

    public sealed record ListMcpConnectionsQuery(Guid ProjectId)
        : IQuery<IReadOnlyList<McpConnectionView>>;
}

namespace PlaceContext.Application.Mcp
{
    public sealed record McpConnectionInfo(
        Guid Id,
        string Name,
        string Transport,
        string? EndpointUrl,
        bool Enabled);

    public sealed record McpToolDefinition(
        string Name,
        string? Description,
        JsonElement? InputSchema);

    public sealed record McpToolResult(
        bool Success,
        string? Content,
        string? Error,
        JsonElement? RawContent = null);

    /// <summary>Interacts with configured external MCP servers.</summary>
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
}
