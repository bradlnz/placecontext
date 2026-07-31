using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfCrmClientRepository : ICrmClientRepository
{
    private readonly AppDbContext _db;

    public EfCrmClientRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(CrmClient client, CancellationToken ct = default)
        => await _db.CrmClients.AddAsync(ToRow(client), ct);

    public async Task UpdateAsync(CrmClient client, CancellationToken ct = default)
    {
        var row = await _db.CrmClients.FindAsync(new object[] { client.Id }, ct);
        if (row is null) return;
        row.Name = client.Name;
        row.Company = client.Company;
        row.Email = client.Email;
        row.Phone = client.Phone;
        row.LifecycleStage = client.LifecycleStage.ToString();
        row.Notes = client.Notes;
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
        var normalizedEmail = email?.Trim().ToLower();
        var normalizedPhone = phone?.Trim();
        var row = await _db.CrmClients.AsNoTracking().FirstOrDefaultAsync(c =>
            c.ProjectId == projectId
            && ((normalizedEmail != null && c.Email != null && c.Email.ToLower() == normalizedEmail)
                || (normalizedPhone != null && c.Phone == normalizedPhone)), ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<CrmClient>> ListForProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
        => (await _db.CrmClients.AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.LifecycleStage)
            .ThenBy(c => c.Name)
            .ToListAsync(ct))
            .Select(ToDomain)
            .ToList();

    private static CrmClientRow ToRow(CrmClient client) => new()
    {
        Id = client.Id,
        ProjectId = client.ProjectId,
        Name = client.Name,
        Company = client.Company,
        Email = client.Email,
        Phone = client.Phone,
        LifecycleStage = client.LifecycleStage.ToString(),
        Notes = client.Notes,
        CreatedAt = client.CreatedAt,
        UpdatedAt = client.UpdatedAt,
    };

    private static CrmClient ToDomain(CrmClientRow row)
        => CrmClient.Rehydrate(
            row.Id,
            row.ProjectId,
            row.Name,
            row.Company,
            row.Email,
            row.Phone,
            Enum.TryParse<CustomerLifecycleStage>(row.LifecycleStage, out var stage)
                ? stage
                : CustomerLifecycleStage.Lead,
            row.Notes,
            row.CreatedAt,
            row.UpdatedAt);
}
