using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfCrmClientUserAssignmentRepository : ICrmClientUserAssignmentRepository
{
    private readonly AppDbContext _db;

    public EfCrmClientUserAssignmentRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> ListForCrmUserAsync(
        Guid projectId,
        Guid crmUserId,
        CancellationToken ct = default)
    {
        return await _db.CrmClientUserAssignments
            .AsNoTracking()
            .Where(assignment => assignment.ProjectId == projectId && assignment.CrmUserId == crmUserId)
            .OrderBy(assignment => assignment.ClientId)
            .Select(assignment => assignment.ClientId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ListForClientAsync(
        Guid projectId,
        Guid clientId,
        CancellationToken ct = default)
    {
        return await _db.CrmClientUserAssignments
            .AsNoTracking()
            .Where(assignment => assignment.ProjectId == projectId && assignment.ClientId == clientId)
            .OrderBy(assignment => assignment.CrmUserId)
            .Select(assignment => assignment.CrmUserId)
            .ToListAsync(ct);
    }

    public async Task SetForClientAsync(
        Guid projectId,
        Guid clientId,
        IReadOnlyList<Guid> userIds,
        CancellationToken ct = default)
    {
        var normalized = userIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToHashSet();

        var rows = await _db.CrmClientUserAssignments
            .Where(assignment => assignment.ProjectId == projectId && assignment.ClientId == clientId)
            .ToListAsync(ct);

        var existing = rows.ToDictionary(row => row.CrmUserId);
        var now = DateTimeOffset.UtcNow;

        var removed = rows.Where(row => !normalized.Contains(row.CrmUserId)).ToList();
        if (removed.Count > 0)
            _db.CrmClientUserAssignments.RemoveRange(removed);

        foreach (var row in rows.Where(row => normalized.Contains(row.CrmUserId)))
            row.UpdatedAt = now;

        foreach (var userId in normalized)
        {
            if (existing.ContainsKey(userId))
                continue;

            _db.CrmClientUserAssignments.Add(new CrmClientUserAssignmentRow
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                ClientId = clientId,
                CrmUserId = userId,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
    }
}
