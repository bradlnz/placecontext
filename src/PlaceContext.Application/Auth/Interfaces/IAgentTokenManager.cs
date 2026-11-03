namespace PlaceContext.Application.Auth;

public interface IAgentTokenManager
{
    Task<string> CreateTokenAsync(Guid tenantId, TimeSpan? expiry = null);
    Task<AgentToken?> ConsumeTokenAsync(string token);
    Task<IReadOnlyList<AgentTokenInfo>> ListTokensAsync(Guid tenantId);
}
