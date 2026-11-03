using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Search;
using PlaceContext.Application.Secrets;
namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfSearchDashboardStore : ISearchDashboardStore
{
    private const string Purpose = "opensearch.dashboard.v1";
    private readonly AppDbContext _db;
    private readonly IDataEncryptor _encryptor;

    public EfSearchDashboardStore(AppDbContext db, IDataEncryptor encryptor)
        => (_db, _encryptor) = (db, encryptor);

    public async Task<IReadOnlyList<SearchDashboardRecord>> ListAsync(
        Guid projectId, CancellationToken ct = default)
        => (await _db.OpenSearchDashboards.AsNoTracking()
                .Where(item => item.ProjectId == projectId)
                .OrderBy(item => item.Name)
                .ToListAsync(ct))
            .Select(ToRecord)
            .ToList();

    public async Task<SearchDashboardRecord?> GetAsync(
        Guid id, CancellationToken ct = default)
    {
        var row = await _db.OpenSearchDashboards.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, ct);
        return row is null ? null : ToRecord(row);
    }

    public async Task SaveAsync(
        SearchDashboardRecord item, CancellationToken ct = default)
    {
        var row = await _db.OpenSearchDashboards
            .FirstOrDefaultAsync(existing => existing.Id == item.Id, ct);
        if (row is null)
        {
            row = new SearchDashboardRow { Id = item.Id };
            _db.OpenSearchDashboards.Add(row);
        }
        row.ProjectId = item.ProjectId;
        row.Name = item.Name;
        row.IndexPattern = item.IndexPattern;
        row.QueryText = ProtectNullable(item.QueryText);
        row.BucketField = item.BucketField;
        row.BucketType = item.BucketType;
        row.ChartType = item.ChartType;
        row.MetricType = item.MetricType;
        row.MetricField = item.MetricField;
        row.DateInterval = item.DateInterval;
        row.ChartSpecJson = _encryptor.Protect(item.ChartSpecJson, Purpose);
        row.CreatedAt = item.CreatedAt;
        row.UpdatedAt = item.UpdatedAt;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.OpenSearchDashboards.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (row is null)
        {
            return false;
        }
        _db.OpenSearchDashboards.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private SearchDashboardRecord ToRecord(SearchDashboardRow row) => new(
        row.Id, row.ProjectId, row.Name, row.IndexPattern, UnprotectNullable(row.QueryText),
        row.BucketField, row.BucketType, row.ChartType, row.MetricType,
        row.MetricField, row.DateInterval, _encryptor.Unprotect(row.ChartSpecJson, Purpose),
        row.CreatedAt, row.UpdatedAt);

    private string? ProtectNullable(string? value)
        => value is null ? null : _encryptor.Protect(value, Purpose);
    private string? UnprotectNullable(string? value)
        => value is null ? null : _encryptor.Unprotect(value, Purpose);
}
