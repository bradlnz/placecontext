using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class InMemoryJobTriggerRepository : IJobTriggerRepository
{
    private readonly List<JobTrigger> _store = new();

    public Task AddAsync(JobTrigger trigger, CancellationToken ct = default)
    {
        _store.Add(trigger);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(JobTrigger trigger, CancellationToken ct = default)
        => Task.CompletedTask; // reference mutated in place

    public Task RemoveAsync(Guid triggerId, CancellationToken ct = default)
    {
        _store.RemoveAll(t => t.Id == triggerId);
        return Task.CompletedTask;
    }

    public Task<JobTrigger?> GetByIdAsync(Guid triggerId, CancellationToken ct = default)
        => Task.FromResult(_store.FirstOrDefault(t => t.Id == triggerId));

    public Task<IReadOnlyList<JobTrigger>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<JobTrigger>>(
            _store.Where(t => t.ProjectId == projectId).OrderBy(t => t.CreatedAt).ToList());

    public Task<IReadOnlyList<JobTrigger>> ListForJobAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<JobTrigger>>(
            _store.Where(t => t.JobId == jobId).OrderBy(t => t.CreatedAt).ToList());

    public Task<IReadOnlyList<JobTrigger>> ListDueSchedulesAsync(DateTimeOffset now, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<JobTrigger>>(_store.Where(t => t.IsDue(now)).ToList());

    public Task<IReadOnlyList<JobTrigger>> ListForEventAsync(string eventName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<JobTrigger>>(_store.Where(t => t.MatchesEvent(eventName)).ToList());
}
