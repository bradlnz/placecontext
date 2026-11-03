namespace PlaceContext.Application.Mcp;

public sealed class McpOAuthAccessToken(string accessToken, DateTimeOffset? expiresAt)
{
    public string AccessToken { get; } = accessToken;
    public DateTimeOffset? ExpiresAt { get; } = expiresAt;
}
