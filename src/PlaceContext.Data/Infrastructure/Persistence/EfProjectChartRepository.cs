using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Data.Infrastructure.Persistence;

public sealed class EfProjectChartRepository : IProjectChartRepository
{
    private readonly AppDbContext _db;
    private readonly IDataEncryptor _enc;
    private static string P => DataEncryptionPurpose.Chart;

    public EfProjectChartRepository(AppDbContext db, IDataEncryptor enc) => (_db, _enc) = (db, enc);

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
            existing.Html = _enc.Protect(chart.Html, P);
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

    private ProjectChartRow ToRow(ProjectChart c) => new()
    {
        Id = c.Id,
        ProjectId = c.ProjectId,
        TableName = c.TableName,
        Html = _enc.Protect(c.Html, P),
        GeneratedAt = c.GeneratedAt,
    };

    private ProjectChart ToDomain(ProjectChartRow r)
        => ProjectChart.Rehydrate(r.Id, r.ProjectId, r.TableName, _enc.Unprotect(r.Html, P), r.GeneratedAt);
}
