namespace PlaceContext.Jobs.Infrastructure.Caching;

/// <summary>
/// Distributed cache for job run shard results. Keeps heavy ShardResultsJson payloads out of
/// Postgres — the DB retains lightweight metadata (status, counts, timestamps) while the full
/// encrypted JSON blob lives in Redis.
/// </summary>
public interface IJobRunCache
{
    Task<string?> GetShardResultsJsonAsync(Guid runId, CancellationToken ct = default);
    Task SetShardResultsJsonAsync(Guid runId, string encryptedJson, CancellationToken ct = default);
    Task RemoveShardResultsJsonAsync(Guid runId, CancellationToken ct = default);
}
