namespace PlaceContext.Identity.OAuth;

/// <summary>Identity-local wire representation of MCP's internal OAuth connection response.</summary>
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
