namespace PlaceContext.Application.Ports;

/// <summary>Hot storage for the accumulated JSON envelope of a running chain.</summary>
public interface IChainContextStore
{
    Task<string?> GetAsync(Guid chainRunId, CancellationToken ct = default);
    Task SetAsync(Guid chainRunId, string? context, CancellationToken ct = default);
    Task RemoveAsync(Guid chainRunId, CancellationToken ct = default);
}
