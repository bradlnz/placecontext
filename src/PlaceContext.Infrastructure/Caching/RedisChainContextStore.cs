using Microsoft.Extensions.Caching.Distributed;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Caching;

/// <summary>Redis-backed hot chain envelope. Values are encrypted before leaving the process.</summary>
public sealed class RedisChainContextStore : IChainContextStore
{
    private readonly IDistributedCache _cache;
    private readonly IDataEncryptor _encryptor;
    private static readonly DistributedCacheEntryOptions Options = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7),
    };

    public RedisChainContextStore(IDistributedCache cache, IDataEncryptor encryptor)
        => (_cache, _encryptor) = (cache, encryptor);

    public async Task<string?> GetAsync(Guid chainRunId, CancellationToken ct = default)
    {
        var value = await _cache.GetStringAsync(Key(chainRunId), ct);
        return value is null ? null : _encryptor.Unprotect(value, IDataEncryptor.Purpose.ChainRun);
    }

    public async Task SetAsync(Guid chainRunId, string? context, CancellationToken ct = default)
    {
        if (context is null)
            await _cache.RemoveAsync(Key(chainRunId), ct);
        else
            await _cache.SetStringAsync(Key(chainRunId),
                _encryptor.Protect(context, IDataEncryptor.Purpose.ChainRun), Options, ct);
    }

    public Task RemoveAsync(Guid chainRunId, CancellationToken ct = default)
        => _cache.RemoveAsync(Key(chainRunId), ct);

    private static string Key(Guid chainRunId) => $"pc:chain-context:{chainRunId:N}";
}
