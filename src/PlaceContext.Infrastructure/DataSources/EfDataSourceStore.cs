using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.DataSources;

public sealed class EfDataSourceStore(AppDbContext db, IDataEncryptor encryption, ICurrentTenant tenant, ISourceQueueDelivery? delivery = null) : IDataSourceStore
{
    private string Purpose(Guid id) => $"data-source.v1/{tenant.TenantId:N}/{id:N}";

    public async Task<IReadOnlyList<DataSourceDefinition>> ListAsync(Guid projectId, CancellationToken ct)
        => (await db.DataSources.AsNoTracking().Where(row => row.ProjectId == projectId).ToListAsync(ct))
            .Select(View).OrderBy(source => source.Name).ToArray();

    public async Task<DataSourceDefinition> GetAsync(Guid projectId, Guid sourceId, CancellationToken ct)
        => View(await FindAsync(projectId, sourceId, ct));

    public async Task<DataSourceDefinition> CreateAsync(DataSourceDefinition source, string connectionString, CancellationToken ct)
    {
        DataSourceRules.Validate(source);
        var connection = SourceSettings.Validate(source.Provider, connectionString);
        if (await db.DataSources.CountAsync(row => row.ProjectId == source.ProjectId, ct) >= 10)
        {
            throw new InvalidOperationException("Sources support up to 10 sources per project.");
        }
        var row = new DataSourceRow
        {
            Id = source.Id, ProjectId = source.ProjectId, Version = source.Version,
            DefinitionCipher = encryption.Protect(JsonSerializer.Serialize(source), Purpose(source.Id)),
            ConnectionCipher = encryption.Protect(JsonSerializer.Serialize(connection), Purpose(source.Id) + "/connection")
        };
        db.DataSources.Add(row);
        await db.SaveChangesAsync(ct);
        return View(row);
    }

    public async Task<IReadOnlyList<SourceDocument>> ReadDocumentsAsync(Guid projectId, Guid sourceId, CancellationToken ct)
    {
        await FindAsync(projectId, sourceId, ct);
        var rows = await db.SourceDocuments.AsNoTracking().Where(row => row.SourceId == sourceId)
            .Take(DataSourceRules.MaxRecords + 1).ToListAsync(ct);
        if (rows.Count > DataSourceRules.MaxRecords)
        {
            throw new InvalidOperationException("Source exceeds the supported record limit.");
        }
        return rows.Select(row => Decode<SourceDocument>(row.ContentCipher, sourceId)).ToArray();
    }

    public async Task CommitAsync(DataSourceDefinition source, IReadOnlyList<SourceDocument> documents, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await using var transaction = source.Queue is not null ? await db.Database.BeginTransactionAsync(ct) : null;
            var row = await FindAsync(source.ProjectId, source.Id, ct);
            db.Entry(row).Property(value => value.Version).OriginalValue = source.Version;
            row.Version = Guid.NewGuid();
            row.DefinitionCipher = encryption.Protect(JsonSerializer.Serialize(source), Purpose(source.Id));
            row.SyncedAt = now;
            row.RecordCount = documents.Count;
            var existing = await db.SourceDocuments.Where(value => value.SourceId == source.Id).ToDictionaryAsync(value => value.KeyHash, ct);
            var changed = new List<SourceDocument>();
            foreach (var document in documents)
            {
                var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document.Key)));
                if (existing.Remove(key, out var old))
                {
                    var previous = Decode<SourceDocument>(old.ContentCipher, source.Id);
                    if (document.ValidationError is null && (previous.Fingerprint != document.Fingerprint || previous.ValidationError is not null))
                    {
                        changed.Add(document);
                    }
                    var serialized = JsonSerializer.Serialize(document);
                    if (encryption.Unprotect(old.ContentCipher, Purpose(source.Id)) != serialized)
                    {
                        old.ContentCipher = encryption.Protect(serialized, Purpose(source.Id));
                    }
                    continue;
                }
                if (document.ValidationError is null)
                {
                    changed.Add(document);
                }
                db.SourceDocuments.Add(new SourceDocumentRow
                {
                    SourceId = source.Id, KeyHash = key,
                    ContentCipher = encryption.Protect(JsonSerializer.Serialize(document), Purpose(source.Id))
                });
            }
            db.SourceDocuments.RemoveRange(existing.Values);
            await db.SaveChangesAsync(ct);
            if (source.Queue is not null)
            {
                var deleted = existing.Values.Select(value => Decode<SourceDocument>(value.ContentCipher, source.Id))
                    .Select(document => document.Key).ToArray();
                await (delivery ?? throw new InvalidOperationException("Queue delivery is unavailable."))
                    .PublishAsync(source with { Version = row.Version }, changed, deleted, ct);
                await transaction!.CommitAsync(ct);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw new InvalidOperationException("Another sync or deletion changed this source. Retry the sync.");
        }
        catch
        {
            // A later status update must never commit documents left tracked by a failed save.
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<DataSourceDefinition> UpdateAsync(DataSourceDefinition source, CancellationToken ct)
    {
        DataSourceRules.Validate(source);
        var row = await FindAsync(source.ProjectId, source.Id, ct);
        db.Entry(row).Property(value => value.Version).OriginalValue = source.Version;
        row.Version = Guid.NewGuid();
        row.DefinitionCipher = encryption.Protect(JsonSerializer.Serialize(source), Purpose(source.Id));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw new InvalidOperationException("Another run or configuration change updated this source. Retry.");
        }
        return View(row);
    }

    public async Task DeleteAsync(Guid projectId, Guid sourceId, CancellationToken ct)
    {
        var source = await FindAsync(projectId, sourceId, ct);
        db.SourceDocuments.RemoveRange(await db.SourceDocuments.Where(row => row.SourceId == sourceId).ToListAsync(ct));
        db.DataSources.Remove(source);
        await db.SaveChangesAsync(ct);
    }

    private async Task<DataSourceRow> FindAsync(Guid projectId, Guid sourceId, CancellationToken ct)
        => await db.DataSources.SingleOrDefaultAsync(row => row.Id == sourceId && row.ProjectId == projectId, ct)
            ?? throw new InvalidOperationException("Source not found in this project.");

    private T Decode<T>(string value, Guid sourceId)
        => JsonSerializer.Deserialize<T>(encryption.Unprotect(value, Purpose(sourceId)))
            ?? throw new InvalidOperationException("Source data could not be decrypted.");

    private DataSourceDefinition View(DataSourceRow row)
        => Decode<DataSourceDefinition>(row.DefinitionCipher, row.Id) with
        {
            Version = row.Version, SyncedAt = row.SyncedAt, RecordCount = row.RecordCount
        };
}
