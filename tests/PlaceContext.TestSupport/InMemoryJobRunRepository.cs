using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.TestSupport;

public sealed class InMemoryJobRunRepository : IJobRunRepository
{
    private readonly List<JobRun> _store = new();

    public Task AddAsync(JobRun run, CancellationToken ct = default)
    {
        _store.Add(run);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(JobRun run, CancellationToken ct = default)
    {
        // In-memory: the reference is already updated in place.
        return Task.CompletedTask;
    }

    public Task<JobRun?> GetByIdAsync(Guid runId, CancellationToken ct = default)
        => Task.FromResult(_store.FirstOrDefault(r => r.Id == runId));

    public Task<IReadOnlyList<JobRun>> ListForJobAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<JobRun>>(
            _store.Where(r => r.JobId == jobId).OrderByDescending(r => r.StartedAt).ToList());

    public Task<IReadOnlyList<JobRun>> ListRecentAsync(int take, CancellationToken ct = default, Guid? projectId = null)
        => Task.FromResult<IReadOnlyList<JobRun>>(
            _store.Where(r => projectId == null || r.ProjectId == projectId).OrderByDescending(r => r.StartedAt).Take(take).ToList());
    public Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(Guid projectId, DateTimeOffset since, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, int>>(_store
            .Where(r => r.ProjectId == projectId && (r.StartedAt >= since || r.Status.ToString() is "Running" or "Queued"))
            .GroupBy(r => r.Status.ToString()).ToDictionary(g => g.Key, g => g.Count()));
}
