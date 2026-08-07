using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfCrmClientRepository : ICrmClientRepository
{
    private readonly AppDbContext _db;
    private readonly IDataEncryptor _encryptor;
    private static string Purpose => IDataEncryptor.Purpose.CrmClient;

    public EfCrmClientRepository(AppDbContext db, IDataEncryptor encryptor)
        => (_db, _encryptor) = (db, encryptor);

    public async Task AddAsync(CrmClient client, CancellationToken ct = default)
        => await _db.CrmClients.AddAsync(ToRow(client), ct);

    public async Task UpdateAsync(CrmClient client, CancellationToken ct = default)
    {
        var row = await _db.CrmClients.FindAsync(new object[] { client.Id }, ct);
        if (row is null) return;
        row.Name = Protect(client.Name);
        row.Company = ProtectNullable(client.Company);
        row.Email = ProtectNullable(client.Email);
        row.Phone = ProtectNullable(client.Phone);
        row.LifecycleStage = client.LifecycleStage.ToString();
        row.Notes = ProtectNullable(client.Notes);
        row.CustomerPortalEnabled = client.CustomerPortalEnabled;
        row.CustomerPortalSlug = client.CustomerPortalSlug;
        row.CustomerPortalDomain = client.CustomerPortalDomain;
        row.CustomerPortalBrandName = client.CustomerPortalBrandName;
        row.CustomerPortalLogoUrl = client.CustomerPortalLogoUrl;
        row.UpdatedAt = client.UpdatedAt;
    }

    public async Task DeleteAsync(Guid clientId, CancellationToken ct = default)
    {
        var communications = await _db.CrmCommunications.Where(r => r.ClientId == clientId).ToListAsync(ct);
        _db.CrmCommunications.RemoveRange(communications);
        var artifacts = await _db.CrmClientArtifacts.Where(r => r.ClientId == clientId).ToListAsync(ct);
        _db.CrmClientArtifacts.RemoveRange(artifacts);
        var chainRuns = await _db.CrmChainRuns.Where(r => r.ClientId == clientId).ToListAsync(ct);
        _db.CrmChainRuns.RemoveRange(chainRuns);
        var links = await _db.CrmJobRuns.Where(r => r.ClientId == clientId).ToListAsync(ct);
        _db.CrmJobRuns.RemoveRange(links);
        var assignments = await _db.CrmClientJobChainAssignments
            .Where(r => r.ClientId == clientId)
            .ToListAsync(ct);
        _db.CrmClientJobChainAssignments.RemoveRange(assignments);
        var row = await _db.CrmClients.FindAsync(new object[] { clientId }, ct);
        if (row is not null) _db.CrmClients.Remove(row);
    }

    public async Task<CrmClient?> GetByIdAsync(Guid clientId, CancellationToken ct = default)
    {
        var row = await _db.CrmClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<CrmClient?> FindByContactAsync(
        Guid projectId,
        string? email,
        string? phone,
        CancellationToken ct = default)
    {
        var normalizedEmail = email?.Trim();
        var normalizedPhone = phone?.Trim();
        if (normalizedEmail is null && normalizedPhone is null) return null;

        // Data Protection uses randomized authenticated encryption, so equality over ciphertext is
        // deliberately impossible. Scope by the non-sensitive ProjectId in SQL, then compare the
        // small CRM candidate set after decrypting in-process. This also keeps legacy plaintext rows
        // readable until the startup backfill rewrites them.
        var rows = await _db.CrmClients.AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .ToListAsync(ct);
        return rows.Select(ToDomain).FirstOrDefault(client =>
            (normalizedEmail is not null
                && string.Equals(client.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            || (normalizedPhone is not null
                && string.Equals(client.Phone, normalizedPhone, StringComparison.Ordinal)));
    }

    public async Task<IReadOnlyList<CrmClient>> ListForProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
        => (await _db.CrmClients.AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .ToListAsync(ct))
            .Select(ToDomain)
            .OrderBy(c => c.LifecycleStage.ToString(), StringComparer.Ordinal)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

    private CrmClientRow ToRow(CrmClient client) => new()
    {
        Id = client.Id,
        ProjectId = client.ProjectId,
        Name = Protect(client.Name),
        Company = ProtectNullable(client.Company),
        Email = ProtectNullable(client.Email),
        Phone = ProtectNullable(client.Phone),
        LifecycleStage = client.LifecycleStage.ToString(),
        Notes = ProtectNullable(client.Notes),
        CustomerPortalEnabled = client.CustomerPortalEnabled,
        CustomerPortalSlug = client.CustomerPortalSlug,
        CustomerPortalDomain = client.CustomerPortalDomain,
        CustomerPortalBrandName = client.CustomerPortalBrandName,
        CustomerPortalLogoUrl = client.CustomerPortalLogoUrl,
        CreatedAt = client.CreatedAt,
        UpdatedAt = client.UpdatedAt,
    };

    private CrmClient ToDomain(CrmClientRow row)
        => CrmClient.Rehydrate(
            row.Id,
            row.ProjectId,
            Unprotect(row.Name),
            UnprotectNullable(row.Company),
            UnprotectNullable(row.Email),
            UnprotectNullable(row.Phone),
            Enum.TryParse<CustomerLifecycleStage>(row.LifecycleStage, out var stage)
                ? stage
                : CustomerLifecycleStage.Lead,
            UnprotectNullable(row.Notes),
            row.CustomerPortalEnabled,
            row.CustomerPortalSlug,
            row.CustomerPortalDomain,
            row.CustomerPortalBrandName,
            row.CustomerPortalLogoUrl,
            row.CreatedAt,
            row.UpdatedAt);

    private string Protect(string value) => _encryptor.Protect(value, Purpose);
    private string Unprotect(string value) => _encryptor.Unprotect(value, Purpose);
    private string? ProtectNullable(string? value) => value is null ? null : Protect(value);
    private string? UnprotectNullable(string? value) => value is null ? null : Unprotect(value);
}
