using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfWorkItemRepository : IWorkItemRepository
{
    private readonly AppDbContext _db;
    public EfWorkItemRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(WorkItem item, CancellationToken ct = default)
        => await _db.WorkItems.AddAsync(new WorkItemRow
        {
            Id = item.Id,
            ProjectId = item.ProjectId.Value,
            Title = item.Title,
            Detail = item.Detail,
            Priority = (int)item.Priority,
            Status = item.Status.ToString(),
            CreatedAt = item.CreatedAt,
            ClaimedAt = item.ClaimedAt,
            CompletedAt = item.CompletedAt,
        }, ct);

    public async Task UpdateAsync(WorkItem item, CancellationToken ct = default)
    {
        var row = await _db.WorkItems.FirstOrDefaultAsync(x => x.Id == item.Id, ct);
        if (row is null) return;
        row.Status = item.Status.ToString();
        row.ClaimedAt = item.ClaimedAt;
        row.CompletedAt = item.CompletedAt;
    }

    public async Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.WorkItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<WorkItem>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var rows = await _db.WorkItems.AsNoTracking()
            .Where(x => x.ProjectId == projectId.Value)
            .OrderBy(x => x.Status == "Done")
            .ThenByDescending(x => x.Priority)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<WorkItem?> NextQueuedAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var row = await _db.WorkItems.AsNoTracking()
            .Where(x => x.ProjectId == projectId.Value && x.Status == "Queued")
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : ToDomain(row);
    }

    private static WorkItem ToDomain(WorkItemRow r) => WorkItem.Rehydrate(
        r.Id, ProjectId.From(r.ProjectId), r.Title, r.Detail, (WorkItemPriority)r.Priority,
        Enum.Parse<WorkItemStatus>(r.Status), r.CreatedAt, r.ClaimedAt, r.CompletedAt);
}
