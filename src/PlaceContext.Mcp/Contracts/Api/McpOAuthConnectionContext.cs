namespace PlaceContext.Mcp.Contracts.Api;

/// <summary>Internal OAuth context exposed only to the Identity service.</summary>
public sealed record McpOAuthConnectionContext(
    Guid Id,
    string Name,
    string? EndpointUrl,
    string? AuthType,
    string? OAuthClientId,
    string? OAuthScopes,
    string? OAuthAccessToken,
    string? OAuthRefreshToken,
    DateTimeOffset? OAuthTokenExpiresAt);
