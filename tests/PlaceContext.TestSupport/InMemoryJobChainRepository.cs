using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.TestSupport;

public sealed class InMemoryJobChainRepository : IJobChainRepository
{
    private readonly List<JobChain> _store = new();

    public Task AddAsync(JobChain chain, CancellationToken ct = default)
    {
        _store.Add(chain);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(JobChain chain, CancellationToken ct = default)
        => Task.CompletedTask; // reference mutated in place

    public Task RemoveAsync(Guid chainId, CancellationToken ct = default)
    {
        _store.RemoveAll(c => c.Id == chainId);
        return Task.CompletedTask;
    }

    public Task<JobChain?> GetByIdAsync(Guid chainId, CancellationToken ct = default)
        => Task.FromResult(_store.FirstOrDefault(c => c.Id == chainId));

    public Task<IReadOnlyList<JobChain>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<JobChain>>(
            _store.Where(c => c.ProjectId == projectId).OrderBy(c => c.CreatedAt).ToList());
}
