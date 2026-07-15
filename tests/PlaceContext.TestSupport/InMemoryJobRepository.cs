using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.TestSupport;

public sealed class InMemoryJobRepository : IJobRepository
{
    private readonly List<Job> _store = new();

    public Task AddAsync(Job job, CancellationToken ct = default)
    {
        _store.Add(job);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Job job, CancellationToken ct = default)
    {
        // In-memory: the reference is already mutated in place by Job.Update().
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid jobId, CancellationToken ct = default)
    {
        _store.RemoveAll(j => j.Id == jobId);
        return Task.CompletedTask;
    }

    public Task<Job?> GetByIdAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult(_store.FirstOrDefault(j => j.Id == jobId));

    public Task<IReadOnlyList<Job>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Job>>(
            _store.Where(j => j.ProjectId == projectId).OrderBy(j => j.CreatedAt).ToList());
}
