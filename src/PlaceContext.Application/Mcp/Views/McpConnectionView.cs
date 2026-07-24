namespace PlaceContext.Application.Dtos;

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
    DateTimeOffset CreatedAt);
