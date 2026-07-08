using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of <see cref="JobRun"/> records (project-scoped, tenant-filtered).</summary>
public interface IJobRunRepository
{
    Task AddAsync(JobRun run, CancellationToken ct = default);
    Task UpdateAsync(JobRun run, CancellationToken ct = default);
    Task<JobRun?> GetByIdAsync(Guid runId, CancellationToken ct = default);
    Task<IReadOnlyList<JobRun>> ListForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>The most recent runs across every job the tenant can see — feeds the reporting view.</summary>
    Task<IReadOnlyList<JobRun>> ListRecentAsync(int take, CancellationToken ct = default);
}
