using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of project work-queue items.</summary>
public interface IWorkItemRepository
{
    Task AddAsync(WorkItem item, CancellationToken ct = default);
    Task UpdateAsync(WorkItem item, CancellationToken ct = default);
    Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WorkItem>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default);
    /// <summary>The next queued item for a project (highest priority, then oldest), or null if the queue is empty.</summary>
    Task<WorkItem?> NextQueuedAsync(ProjectId projectId, CancellationToken ct = default);
}
