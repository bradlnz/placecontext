using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;

namespace PlaceContext.Data.Infrastructure.Persistence;

public sealed class EfSavedQueryStore : ISavedQueryStore
{
    private readonly DataDbContext _db;

    public EfSavedQueryStore(DataDbContext db) => _db = db;

    public async Task<IReadOnlyList<SavedQueryRecord>> ListAsync(
        Guid projectId, CancellationToken ct = default)
        => (await _db.SavedQueries.AsNoTracking()
                .Where(item => item.ProjectId == projectId)
                .OrderBy(item => item.Name)
                .ToListAsync(ct))
            .Select(ToRecord)
            .ToList();

    public async Task<SavedQueryRecord?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.SavedQueries.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, ct);
        return row is null ? null : ToRecord(row);
    }

    public async Task<SavedQueryRecord?> FindByNameAsync(
        Guid projectId, string name, CancellationToken ct = default)
    {
        var row = await _db.SavedQueries.AsNoTracking()
            .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.Name == name, ct);
        return row is null ? null : ToRecord(row);
    }

    public async Task SaveAsync(SavedQueryRecord item, CancellationToken ct = default)
    {
        var row = await _db.SavedQueries.FirstOrDefaultAsync(existing => existing.Id == item.Id, ct);
        if (row is null)
        {
            row = new SavedQueryRow { Id = item.Id };
            _db.SavedQueries.Add(row);
        }

        row.ProjectId = item.ProjectId;
        row.Name = item.Name;
        row.Sql = item.Sql;
        row.CreatedAt = item.CreatedAt;
        row.UpdatedAt = item.UpdatedAt;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.SavedQueries.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (row is null)
            return false;

        _db.SavedQueries.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static SavedQueryRecord ToRecord(SavedQueryRow row) => new(
        row.Id, row.ProjectId, row.Name, row.Sql, row.CreatedAt, row.UpdatedAt);
}
