namespace PlaceContext.Application.Mcp;

public sealed record McpOAuthConnection(Guid Id, string Name, string? EndpointUrl, string? OAuthClientId,
    string? OAuthScopes, bool IsOAuth, bool HasRefreshToken, bool HasAccessToken, bool TokenExpired);
