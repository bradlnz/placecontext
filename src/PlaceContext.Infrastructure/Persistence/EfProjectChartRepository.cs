using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfProjectChartRepository : IProjectChartRepository
{
    private readonly AppDbContext _db;
    public EfProjectChartRepository(AppDbContext db) => _db = db;

    public async Task UpsertAsync(ProjectChart chart, CancellationToken ct = default)
    {
        var existing = await _db.ProjectCharts
            .FirstOrDefaultAsync(r => r.ProjectId == chart.ProjectId && r.TableName == chart.TableName, ct);
        if (existing is null)
        {
            await _db.ProjectCharts.AddAsync(ToRow(chart), ct);
        }
        else
        {
            existing.Html = chart.Html;
            existing.GeneratedAt = chart.GeneratedAt;
        }
    }

    public async Task<IReadOnlyList<ProjectChart>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var rows = await _db.ProjectCharts.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.TableName)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task DeleteAsync(Guid projectId, string tableName, CancellationToken ct = default)
    {
        var row = await _db.ProjectCharts
            .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.TableName == tableName, ct);
        if (row is not null) _db.ProjectCharts.Remove(row);
    }

    public async Task DeleteForProjectAsync(Guid projectId, IReadOnlyCollection<string> keepTables, CancellationToken ct = default)
    {
        // "sql:{name}" slots are user-defined Charts, not table charts — the table sweep
        // must never prune them.
        var stale = await _db.ProjectCharts
            .Where(r => r.ProjectId == projectId && !keepTables.Contains(r.TableName)
                        && !r.TableName.StartsWith("sql:"))
            .ToListAsync(ct);
        _db.ProjectCharts.RemoveRange(stale);
    }

    private static ProjectChartRow ToRow(ProjectChart c) => new()
    {
        Id = c.Id,
        ProjectId = c.ProjectId,
        TableName = c.TableName,
        Html = c.Html,
        GeneratedAt = c.GeneratedAt,
    };

    private static ProjectChart ToDomain(ProjectChartRow r)
        => ProjectChart.Rehydrate(r.Id, r.ProjectId, r.TableName, r.Html, r.GeneratedAt);
}
