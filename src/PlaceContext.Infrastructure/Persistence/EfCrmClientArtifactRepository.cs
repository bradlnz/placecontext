using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfCrmClientArtifactRepository : ICrmClientArtifactRepository
{
    private readonly AppDbContext _db;

    public EfCrmClientArtifactRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(CrmClientArtifact artifact, CancellationToken ct = default)
        => await _db.CrmClientArtifacts.AddAsync(ToRow(artifact), ct);

    public async Task<CrmClientArtifact?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.CrmClientArtifacts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public Task<bool> ExistsForSourceAsync(
        Guid clientId,
        Guid sourceArtifactId,
        CancellationToken ct = default)
        => _db.CrmClientArtifacts.AsNoTracking()
            .AnyAsync(x => x.ClientId == clientId && x.SourceArtifactId == sourceArtifactId, ct);

    public async Task<IReadOnlyList<CrmClientArtifact>> ListForClientAsync(
        Guid clientId,
        int take = 200,
        CancellationToken ct = default)
        => (await _db.CrmClientArtifacts.AsNoTracking()
            .Where(x => x.ClientId == clientId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync(ct))
            .Select(ToDomain)
            .ToList();

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.CrmClientArtifacts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is not null) _db.CrmClientArtifacts.Remove(row);
    }

    private static CrmClientArtifactRow ToRow(CrmClientArtifact value) => new()
    {
        Id = value.Id,
        ProjectId = value.ProjectId,
        ClientId = value.ClientId,
        SourceArtifactId = value.SourceArtifactId,
        ChainRunId = value.ChainRunId,
        Title = value.Title,
        Bucket = value.Bucket,
        ObjectKey = value.ObjectKey,
        ContentType = value.ContentType,
        SizeBytes = value.SizeBytes,
        CreatedAt = value.CreatedAt,
    };

    private static CrmClientArtifact ToDomain(CrmClientArtifactRow row)
        => CrmClientArtifact.Rehydrate(
            row.Id, row.ProjectId, row.ClientId, row.SourceArtifactId, row.ChainRunId,
            row.Title, row.Bucket, row.ObjectKey, row.ContentType, row.SizeBytes, row.CreatedAt);
}
