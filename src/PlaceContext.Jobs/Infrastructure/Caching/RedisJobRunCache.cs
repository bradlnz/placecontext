using Microsoft.Extensions.Caching.Distributed;

namespace PlaceContext.Jobs.Infrastructure.Caching;

/// <summary>
/// Redis-backed <see cref="IJobRunCache"/>. Stores encrypted ShardResultsJson blobs keyed by run ID.
/// TTL is 7 days — old runs that haven't been accessed fall off automatically.
/// </summary>
public sealed class RedisJobRunCache : IJobRunCache
{
    private readonly IDistributedCache _cache;
    private static readonly DistributedCacheEntryOptions Opts = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7),
    };

    public RedisJobRunCache(IDistributedCache cache) => _cache = cache;

    public async Task<string?> GetShardResultsJsonAsync(Guid runId, CancellationToken ct = default)
        => await _cache.GetStringAsync(Key(runId), ct);

    public async Task SetShardResultsJsonAsync(Guid runId, string encryptedJson, CancellationToken ct = default)
        => await _cache.SetStringAsync(Key(runId), encryptedJson, Opts, ct);

    public async Task RemoveShardResultsJsonAsync(Guid runId, CancellationToken ct = default)
        => await _cache.RemoveAsync(Key(runId), ct);

    private static string Key(Guid runId) => $"pc:run:{runId:N}";
}
