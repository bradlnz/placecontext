using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Caching;

/// <summary>Fallback when Redis is not configured; the chain run checkpoint remains authoritative.</summary>
public sealed class NullChainContextStore : IChainContextStore
{
    public Task<string?> GetAsync(Guid chainRunId, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task SetAsync(Guid chainRunId, string? context, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RemoveAsync(Guid chainRunId, CancellationToken ct = default)
        => Task.CompletedTask;
}
