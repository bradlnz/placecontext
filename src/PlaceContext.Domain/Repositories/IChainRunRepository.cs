using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of <see cref="ChainRun"/> records (tenant-filtered) — the pipeline history.</summary>
public interface IChainRunRepository
{
    Task AddAsync(ChainRun run, CancellationToken ct = default);
    Task UpdateAsync(ChainRun run, CancellationToken ct = default);
    Task<ChainRun?> GetByIdAsync(Guid chainRunId, CancellationToken ct = default);

    /// <summary>A chain's runs, newest first.</summary>
    Task<IReadOnlyList<ChainRun>> ListForChainAsync(Guid chainId, int take, CancellationToken ct = default);
}
