using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Infrastructure.Cluster;

public sealed class InMemoryAgentTokenManager : IAgentTokenManager
{
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(30);
    private const string Prefix = "pca_";

    private readonly ConcurrentDictionary<string, TokenEntry> _tokens = new(StringComparer.Ordinal);

    public Task<string> CreateTokenAsync(Guid tenantId, TimeSpan? expiry = null)
    {
        var exp = expiry ?? DefaultExpiry;
        var plaintext = Prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var hash = HashToken(plaintext);
        var entry = new TokenEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TokenPrefix = plaintext[..Math.Min(12, plaintext.Length)],
            Hash = hash,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + exp,
        };
        _tokens[hash] = entry;
        return Task.FromResult(plaintext);
    }

    public Task<AgentToken?> ConsumeTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(Prefix, StringComparison.Ordinal))
            return Task.FromResult<AgentToken?>(null);

        var hash = HashToken(token);
        if (!_tokens.TryGetValue(hash, out var entry))
            return Task.FromResult<AgentToken?>(null);

        if (entry.UsedAt is not null || entry.ExpiresAt < DateTimeOffset.UtcNow)
            return Task.FromResult<AgentToken?>(null);

        entry.UsedAt = DateTimeOffset.UtcNow;
        return Task.FromResult<AgentToken?>(new AgentToken(entry.Id, entry.TenantId, entry.TokenPrefix, entry.ExpiresAt));
    }

    public Task<IReadOnlyList<AgentTokenInfo>> ListTokensAsync(Guid tenantId)
    {
        var list = _tokens.Values
            .Where(t => t.TenantId == tenantId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new AgentTokenInfo(t.Id, t.TokenPrefix, t.CreatedAt, t.ExpiresAt, t.UsedAt))
            .ToList() as IReadOnlyList<AgentTokenInfo>;
        return Task.FromResult(list);
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

}
