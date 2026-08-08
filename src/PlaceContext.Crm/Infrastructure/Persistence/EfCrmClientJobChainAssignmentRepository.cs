using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Crm.Infrastructure.Persistence;

public sealed class EfCrmClientJobChainAssignmentRepository : ICrmClientJobChainAssignmentRepository
{
    private readonly AppDbContext _db;

    public EfCrmClientJobChainAssignmentRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> ListForClientAsync(
        Guid projectId,
        Guid clientId,
        CancellationToken ct = default)
    {
        return await _db.CrmClientJobChainAssignments
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.ClientId == clientId)
            .OrderBy(x => x.ChainId)
            .Select(x => x.ChainId)
            .ToListAsync(ct);
    }

    public async Task SetForClientAsync(
        Guid projectId,
        Guid clientId,
        IReadOnlyList<Guid> chainIds,
        CancellationToken ct = default)
    {
        var normalized = chainIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToHashSet();

        var rows = await _db.CrmClientJobChainAssignments
            .Where(x => x.ProjectId == projectId && x.ClientId == clientId)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var existing = rows.ToDictionary(x => x.ChainId);
        var toRemove = rows.Where(x => !normalized.Contains(x.ChainId)).ToList();
        if (toRemove.Count > 0)
            _db.CrmClientJobChainAssignments.RemoveRange(toRemove);

        foreach (var row in rows.Where(x => normalized.Contains(x.ChainId)))
            row.UpdatedAt = now;

        foreach (var chainId in normalized)
        {
            if (!existing.ContainsKey(chainId))
            {
                _db.CrmClientJobChainAssignments.Add(new CrmClientJobChainAssignmentRow
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    ClientId = clientId,
                    ChainId = chainId,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }
    }
}
