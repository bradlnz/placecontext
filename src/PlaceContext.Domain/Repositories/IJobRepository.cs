using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of <see cref="Job"/> definitions (project-scoped, tenant-filtered).</summary>
public interface IJobRepository
{
    Task AddAsync(Job job, CancellationToken ct = default);
    Task UpdateAsync(Job job, CancellationToken ct = default);
    Task<Job?> GetByIdAsync(Guid jobId, CancellationToken ct = default);
    Task<IReadOnlyList<Job>> ListForProjectAsync(Guid projectId, CancellationToken ct = default);
}
