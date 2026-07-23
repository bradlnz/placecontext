namespace PlaceContext.Infrastructure.Caching;

/// <summary>
/// No-op cache used when Redis is not configured. Falls through to Postgres for all reads.
/// </summary>
public sealed class NullJobRunCache : IJobRunCache
{
    public Task<string?> GetShardResultsJsonAsync(Guid runId, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task SetShardResultsJsonAsync(Guid runId, string encryptedJson, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RemoveShardResultsJsonAsync(Guid runId, CancellationToken ct = default)
        => Task.CompletedTask;
}
