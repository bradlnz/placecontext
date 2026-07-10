using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of <see cref="JobChain"/> definitions (project-scoped, tenant-filtered).</summary>
public interface IJobChainRepository
{
    Task AddAsync(JobChain chain, CancellationToken ct = default);
    Task UpdateAsync(JobChain chain, CancellationToken ct = default);
    Task RemoveAsync(Guid chainId, CancellationToken ct = default);
    Task<JobChain?> GetByIdAsync(Guid chainId, CancellationToken ct = default);
    Task<IReadOnlyList<JobChain>> ListForProjectAsync(Guid projectId, CancellationToken ct = default);
}
