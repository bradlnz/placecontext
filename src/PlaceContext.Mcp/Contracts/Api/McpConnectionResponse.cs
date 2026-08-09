namespace PlaceContext.Mcp.Contracts.Api;

public sealed record McpConnectionResponse(
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
    DateTimeOffset? OAuthTokenExpiresAt,
    string? OAuthClientId,
    string? OAuthScopes);
