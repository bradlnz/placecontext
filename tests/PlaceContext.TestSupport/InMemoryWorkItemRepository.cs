using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class InMemoryWorkItemRepository : IWorkItemRepository
{
    private readonly List<WorkItem> _items = new();

    public Task AddAsync(WorkItem item, CancellationToken ct = default) { _items.Add(item); return Task.CompletedTask; }

    // Items are mutated in place (reference types), so updates are already reflected.
    public Task UpdateAsync(WorkItem item, CancellationToken ct = default) => Task.CompletedTask;

    public Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_items.FirstOrDefault(i => i.Id == id));

    public Task<IReadOnlyList<WorkItem>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WorkItem>>(_items.Where(i => i.ProjectId == projectId).ToList());

    public Task<WorkItem?> NextQueuedAsync(ProjectId projectId, CancellationToken ct = default)
        => Task.FromResult(_items
            .Where(i => i.ProjectId == projectId && i.Status == WorkItemStatus.Queued)
            .OrderByDescending(i => i.Priority)
            .ThenBy(i => i.CreatedAt)
            .FirstOrDefault());
}
