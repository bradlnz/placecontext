using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Tenancy;

public sealed class EfTenantCatalog : ITenantCatalog
{
    private readonly AppDbContext _db;

    public EfTenantCatalog(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<TenantContext>> ListAsync(CancellationToken ct = default)
        => await _db.Tenants.AsNoTracking()
            .Select(row => new TenantContext(row.Id, row.Slug, row.TimeZoneId))
            .ToListAsync(ct);

    public async Task<TenantContext?> FindAsync(Guid tenantId, CancellationToken ct = default)
        => await _db.Tenants.AsNoTracking()
            .Where(row => row.Id == tenantId)
            .Select(row => new TenantContext(row.Id, row.Slug, row.TimeZoneId))
            .FirstOrDefaultAsync(ct);
}
