using PlaceContext.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Tenancy;

public sealed class EfTenantStore : ITenantStore
{
    private readonly AppDbContext _db;
    public EfTenantStore(AppDbContext db) => _db = db;

    public async Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct = default)
    {
        var row = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug, ct);
        return row is null ? null : ToInfo(row);
    }

    public async Task<TenantInfo> GetOrCreateAsync(string slug, CancellationToken ct = default)
    {
        var row = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (row is not null) return ToInfo(row);

        row = new TenantRow
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Name = slug,
            TimeZoneId = "UTC",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _db.Tenants.AddAsync(row, ct);
        try
        {
            await _db.SaveChangesAsync(ct);
            return ToInfo(row);
        }
        catch (DbUpdateException)
        {
            // Provisioning race: a concurrent first-request for this new tenant won the insert.
            _db.Entry(row).State = EntityState.Detached;
            var existing = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug, ct);
            if (existing is null) throw;
            return ToInfo(existing);
        }
    }

    public async Task<IReadOnlyList<TenantInfo>> ListTenantsAsync(int take = 1000, CancellationToken ct = default)
    {
        var rows = await _db.Tenants.AsNoTracking()
            .OrderBy(t => t.Name)
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(ToInfo).ToList();
    }

    public Task<TenantRow?> GetRowAsync(Guid tenantId, CancellationToken ct = default)
        => _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)!;

    public async Task SaveGitHubAsync(Guid tenantId, string githubLogin, string accessToken, CancellationToken ct = default)
    {
        var row = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (row is null) return;
        row.GitHubLogin = githubLogin;
        row.GitHubToken = accessToken;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetTimeZoneAsync(Guid tenantId, string timeZoneId, CancellationToken ct = default)
    {
        var row = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (row is null) return;
        row.TimeZoneId = timeZoneId;
        await _db.SaveChangesAsync(ct);
    }

    private static TenantInfo ToInfo(TenantRow r) => new(r.Id, r.Slug, r.Name, r.TimeZoneId);
}
