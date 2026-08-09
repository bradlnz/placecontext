using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Identity.Infrastructure.Persistence;

namespace PlaceContext.Identity.Infrastructure.Tenancy;

public sealed class EfTenantCatalog(IdentityDbContext db) : ITenantCatalog
{
    public async Task<IReadOnlyList<TenantContext>> ListAsync(CancellationToken ct = default)
        => await db.Tenants.AsNoTracking()
            .Select(row => new TenantContext(row.Id, row.Slug, row.TimeZoneId)).ToListAsync(ct);

    public async Task<TenantContext?> FindAsync(Guid tenantId, CancellationToken ct = default)
        => await db.Tenants.AsNoTracking().Where(row => row.Id == tenantId)
            .Select(row => new TenantContext(row.Id, row.Slug, row.TimeZoneId)).FirstOrDefaultAsync(ct);
}
