namespace PlaceContext.AgentChat.Infrastructure.Persistence;

public sealed class McpConnectionRow : IAgentChatTenantOwned
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Transport { get; set; } = string.Empty;
    public string? EndpointUrl { get; set; }
    public string? Command { get; set; }
    public string? Args { get; set; }
    public string? AuthType { get; set; }
    public string? AuthToken { get; set; }
    public string? AuthHeader { get; set; }
    public bool Enabled { get; set; }
    public string? LastStatus { get; set; }
    public DateTimeOffset? LastConnectedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? OAuthAccessToken { get; set; }
    public string? OAuthRefreshToken { get; set; }
    public DateTimeOffset? OAuthTokenExpiresAt { get; set; }
    public string? OAuthClientId { get; set; }
    public string? OAuthScopes { get; set; }
}
