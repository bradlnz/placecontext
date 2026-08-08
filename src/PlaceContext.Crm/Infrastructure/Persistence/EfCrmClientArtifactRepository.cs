using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Crm.Infrastructure.Persistence;

public sealed class EfCrmClientArtifactRepository : ICrmClientArtifactRepository
{
    private readonly AppDbContext _db;
    private readonly IDataEncryptor _encryptor;
    private static string Purpose => DataEncryptionPurpose.CrmArtifactMetadata;

    public EfCrmClientArtifactRepository(AppDbContext db, IDataEncryptor encryptor)
        => (_db, _encryptor) = (db, encryptor);

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

    private CrmClientArtifactRow ToRow(CrmClientArtifact value) => new()
    {
        Id = value.Id,
        ProjectId = value.ProjectId,
        ClientId = value.ClientId,
        SourceArtifactId = value.SourceArtifactId,
        ChainRunId = value.ChainRunId,
        Title = Protect(value.Title),
        Bucket = value.Bucket,
        ObjectKey = Protect(value.ObjectKey),
        ContentType = value.ContentType,
        SizeBytes = value.SizeBytes,
        CreatedAt = value.CreatedAt,
    };

    private CrmClientArtifact ToDomain(CrmClientArtifactRow row)
        => CrmClientArtifact.Rehydrate(
            row.Id, row.ProjectId, row.ClientId, row.SourceArtifactId, row.ChainRunId,
            Unprotect(row.Title), row.Bucket, Unprotect(row.ObjectKey), row.ContentType,
            row.SizeBytes, row.CreatedAt);

    private string Protect(string value) => _encryptor.Protect(value, Purpose);
    private string Unprotect(string value) => _encryptor.Unprotect(value, Purpose);
}
