using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Data.Infrastructure.Persistence;

public sealed class EfRecordLinkStore : IRecordLinkStore
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public EfRecordLinkStore(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task ReplaceForProjectAsync(Guid projectId, IReadOnlyList<RecordLink> links, CancellationToken ct = default)
    {
        await _db.RecordLinks.Where(r => r.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await InsertAsync(links, ct);
    }

    public async Task ReplaceForTableAsync(Guid projectId, string table, IReadOnlyList<RecordLink> links, CancellationToken ct = default)
    {
        await _db.RecordLinks.Where(r => r.ProjectId == projectId && r.TableName == table).ExecuteDeleteAsync(ct);
        await InsertAsync(links, ct);
    }

    public async Task<IReadOnlyList<RecordLink>> RelatedAsync(Guid projectId, string table, string rowKey,
        int take = 20, CancellationToken ct = default)
    {
        var mine = await _db.RecordLinks.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.TableName == table && r.RowKey == rowKey)
            .Select(r => r.NormalizedValue)
            .Distinct()
            .ToListAsync(ct);
        if (mine.Count == 0) return Array.Empty<RecordLink>();

        return await _db.RecordLinks.AsNoTracking()
            .Where(r => r.ProjectId == projectId && mine.Contains(r.NormalizedValue)
                && !(r.TableName == table && r.RowKey == rowKey))
            .OrderBy(r => r.TableName).ThenBy(r => r.RowKey)
            .Take(Math.Clamp(take, 1, 100))
            .Select(r => Map(r))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RecordLinkGroup>> GroupsAsync(Guid projectId, int take = 50, CancellationToken ct = default)
    {
        // Values with ≥ 2 occurrences, largest first — then one follow-up query for their rows.
        var top = await _db.RecordLinks.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .GroupBy(r => new { r.Kind, r.NormalizedValue })
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count())
            .Take(Math.Clamp(take, 1, 200))
            .Select(g => new { g.Key.Kind, g.Key.NormalizedValue })
            .ToListAsync(ct);
        if (top.Count == 0) return Array.Empty<RecordLinkGroup>();

        var values = top.Select(t => t.NormalizedValue).ToList();
        var rows = await _db.RecordLinks.AsNoTracking()
            .Where(r => r.ProjectId == projectId && values.Contains(r.NormalizedValue))
            .ToListAsync(ct);

        return top.Select(t =>
        {
            var occurrences = rows
                .Where(r => r.Kind == t.Kind && r.NormalizedValue == t.NormalizedValue)
                .Select(Map)
                .ToList();
            var display = occurrences.GroupBy(o => o.DisplayValue).OrderByDescending(g => g.Count()).First().Key;
            return new RecordLinkGroup(t.Kind, t.NormalizedValue, display, occurrences);
        }).ToList();
    }

    private async Task InsertAsync(IReadOnlyList<RecordLink> links, CancellationToken ct)
    {
        if (links.Count == 0) return;
        await _db.RecordLinks.AddRangeAsync(links.Select(l => new RecordLinkRow
        {
            Id = Guid.NewGuid(),
            ProjectId = l.ProjectId,
            Kind = l.Kind,
            NormalizedValue = l.NormalizedValue,
            DisplayValue = l.DisplayValue,
            TableName = l.TableName,
            ColumnName = l.ColumnName,
            RowKey = l.RowKey,
            CreatedAt = _clock.UtcNow,
        }), ct);
        await _db.SaveChangesAsync(ct);
    }

    private static RecordLink Map(RecordLinkRow r)
        => new(r.ProjectId, r.Kind, r.NormalizedValue, r.DisplayValue, r.TableName, r.ColumnName, r.RowKey);
}
