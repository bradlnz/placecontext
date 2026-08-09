using PlaceContext.Operations.Contracts.Backup;
using PlaceContext.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Tenancy;

/// <summary>
/// Adapts the tenant registry row to the Application's <see cref="ITenantSettingsPort"/> — the backup
/// feature's only window onto <see cref="TenantRow"/>, so the Application layer never depends on
/// Infrastructure's <see cref="ITenantStore"/> directly (dependency direction). Mutates the tracked
/// row but does not call <c>SaveChangesAsync</c> itself — the caller's unit of work owns the commit.
/// </summary>
public sealed class EfTenantSettingsPort : ITenantSettingsPort
{
    private readonly AppDbContext _db;
    public EfTenantSettingsPort(AppDbContext db) => _db = db;

    public async Task<TenantSettingsSnapshot> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        return row is null
            ? new TenantSettingsSnapshot("UTC", null, null)
            : new TenantSettingsSnapshot(row.TimeZoneId, row.BrandingJson, row.GitHubLogin);
    }

    public async Task ApplyAsync(Guid tenantId, TenantSettingsSnapshot snapshot, CancellationToken ct = default)
    {
        var row = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (row is null) return; // defensive: the tenant row always exists once resolved

        if (!string.IsNullOrWhiteSpace(snapshot.TimeZoneId)) row.TimeZoneId = snapshot.TimeZoneId;
        if (snapshot.BrandingJson is not null) row.BrandingJson = snapshot.BrandingJson;
        if (!string.IsNullOrWhiteSpace(snapshot.GitHubLogin)) row.GitHubLogin = snapshot.GitHubLogin;
        // GitHubToken is intentionally untouched — it's a live secret, not part of the portable snapshot.
    }
}
