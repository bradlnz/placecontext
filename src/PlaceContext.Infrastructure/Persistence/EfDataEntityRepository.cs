using System.Text.Json;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfDataEntityRepository : IDataEntityRepository
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly AppDbContext _db;

    public EfDataEntityRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(DataEntity entity, CancellationToken ct = default)
        => await _db.DataEntities.AddAsync(ToRow(entity), ct);

    public async Task UpdateAsync(DataEntity entity, CancellationToken ct = default)
    {
        var existing = await _db.DataEntities.FindAsync(new object[] { entity.Id }, ct);
        if (existing is null) return;
        existing.Name = entity.Name;
        existing.TableName = entity.TableName;
        existing.LabelColumn = entity.LabelColumn;
        existing.RelationsJson = SerializeRelations(entity.Relations);
        existing.TagsJson = SerializeTags(entity.Tags);
        existing.UpdatedAt = entity.UpdatedAt;
    }

    public async Task DeleteAsync(Guid entityId, CancellationToken ct = default)
    {
        var existing = await _db.DataEntities.FindAsync(new object[] { entityId }, ct);
        if (existing is not null) _db.DataEntities.Remove(existing);
    }

    public async Task<DataEntity?> GetByIdAsync(Guid entityId, CancellationToken ct = default)
    {
        var row = await _db.DataEntities.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entityId, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<DataEntity>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
        => (await _db.DataEntities.AsNoTracking()
            .Where(e => e.ProjectId == projectId)
            .OrderBy(e => e.Name)
            .ToListAsync(ct)).Select(ToDomain).ToList();

    private static DataEntityRow ToRow(DataEntity e) => new()
    {
        Id = e.Id,
        ProjectId = e.ProjectId,
        Name = e.Name,
        TableName = e.TableName,
        LabelColumn = e.LabelColumn,
        RelationsJson = SerializeRelations(e.Relations),
        TagsJson = SerializeTags(e.Tags),
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

    private static DataEntity ToDomain(DataEntityRow row)
    {
        var relations = (JsonSerializer.Deserialize<List<RelationJson>>(row.RelationsJson, Json) ?? new())
            .Select(r => new EntityRelation(r.Column, r.TargetEntity, r.TargetColumn))
            .ToList();
        var tags = JsonSerializer.Deserialize<List<string>>(row.TagsJson, Json) ?? new();
        return DataEntity.Rehydrate(row.Id, row.ProjectId, row.Name, row.TableName, row.LabelColumn,
            relations, tags, row.CreatedAt, row.UpdatedAt);
    }

    private static string SerializeRelations(IReadOnlyList<EntityRelation> relations)
        => JsonSerializer.Serialize(relations.Select(r => new RelationJson(r.Column, r.TargetEntity, r.TargetColumn)).ToList(), Json);

    private static string SerializeTags(IReadOnlyList<string> tags)
        => JsonSerializer.Serialize(tags, Json);

    private sealed record RelationJson(string Column, string TargetEntity, string TargetColumn);
}
