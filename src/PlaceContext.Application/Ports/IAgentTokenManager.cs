namespace PlaceContext.Application.Ports;

public interface IAgentTokenManager
{
    Task<string> CreateTokenAsync(Guid tenantId, TimeSpan? expiry = null);
    Task<AgentToken?> ConsumeTokenAsync(string token);
    Task<IReadOnlyList<AgentTokenInfo>> ListTokensAsync(Guid tenantId);
}

public sealed record AgentToken(Guid Id, Guid TenantId, string TokenPrefix, DateTimeOffset ExpiresAt);

public sealed record AgentTokenInfo(
    Guid Id,
    string TokenPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt);
