using System.Text.Json;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfDataMappingRepository : IDataMappingRepository
{
    private readonly AppDbContext _db;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public EfDataMappingRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(DataMapping mapping, CancellationToken ct = default)
        => await _db.DataMappings.AddAsync(ToRow(mapping), ct);

    public async Task UpdateAsync(DataMapping mapping, CancellationToken ct = default)
    {
        var existing = await _db.DataMappings.FindAsync(new object[] { mapping.Id }, ct);
        if (existing is null) return;
        existing.TargetTable = mapping.TargetTable;
        existing.RowsPath = mapping.RowsPath;
        existing.FieldsJson = SerializeFields(mapping.Fields);
        existing.Enabled = mapping.Enabled;
        existing.UpdatedAt = mapping.UpdatedAt;
    }

    public async Task DeleteAsync(Guid mappingId, CancellationToken ct = default)
    {
        var existing = await _db.DataMappings.FindAsync(new object[] { mappingId }, ct);
        if (existing is not null) _db.DataMappings.Remove(existing);
    }

    public async Task<DataMapping?> GetByIdAsync(Guid mappingId, CancellationToken ct = default)
    {
        var row = await _db.DataMappings.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mappingId, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<DataMapping>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
        => (await _db.DataMappings.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct)).Select(ToDomain).ToList();

    public async Task<IReadOnlyList<DataMapping>> ListForJobAsync(Guid jobId, CancellationToken ct = default)
        => (await _db.DataMappings.AsNoTracking()
            .Where(m => m.JobId == jobId) // job OR chain — the id spaces never collide
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct)).Select(ToDomain).ToList();

    private static DataMappingRow ToRow(DataMapping m) => new()
    {
        Id = m.Id,
        ProjectId = m.ProjectId,
        JobId = m.JobId,
        SourceKind = m.SourceKind,
        TargetTable = m.TargetTable,
        RowsPath = m.RowsPath,
        FieldsJson = SerializeFields(m.Fields),
        Enabled = m.Enabled,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt,
    };

    private static DataMapping ToDomain(DataMappingRow row)
    {
        var fields = (JsonSerializer.Deserialize<List<FieldJson>>(row.FieldsJson, Json) ?? new List<FieldJson>())
            .Select(f => new DataFieldMapping(f.SourcePath, f.Column, f.Type))
            .ToList();
        return DataMapping.Rehydrate(row.Id, row.ProjectId, row.JobId, row.SourceKind, row.TargetTable, row.RowsPath,
            fields, row.Enabled, row.CreatedAt, row.UpdatedAt);
    }

    private static string SerializeFields(IReadOnlyList<DataFieldMapping> fields)
        => JsonSerializer.Serialize(fields.Select(f => new FieldJson(f.SourcePath, f.Column, f.Type)).ToList(), Json);

    private sealed record FieldJson(string SourcePath, string Column, string Type);
}
