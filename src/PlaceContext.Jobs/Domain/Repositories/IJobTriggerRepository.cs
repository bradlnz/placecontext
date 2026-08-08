using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of <see cref="JobTrigger"/> definitions (project-scoped, tenant-filtered).</summary>
public interface IJobTriggerRepository
{
    Task AddAsync(JobTrigger trigger, CancellationToken ct = default);
    Task UpdateAsync(JobTrigger trigger, CancellationToken ct = default);
    Task RemoveAsync(Guid triggerId, CancellationToken ct = default);
    Task<JobTrigger?> GetByIdAsync(Guid triggerId, CancellationToken ct = default);
    Task<IReadOnlyList<JobTrigger>> ListForProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<JobTrigger>> ListForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Enabled schedule triggers whose NextRunAt is at or before <paramref name="now"/> (current tenant).</summary>
    Task<IReadOnlyList<JobTrigger>> ListDueSchedulesAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Enabled event triggers subscribed to <paramref name="eventName"/> (current tenant).</summary>
    Task<IReadOnlyList<JobTrigger>> ListForEventAsync(string eventName, CancellationToken ct = default);
}
