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
        if (row is null)
        {
            row = new TenantRow
            {
                Id = Guid.NewGuid(),
                Slug = slug,
                Name = slug,
                TimeZoneId = "UTC",
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _db.Tenants.AddAsync(row, ct);
            await _db.SaveChangesAsync(ct);
        }
        return ToInfo(row);
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
