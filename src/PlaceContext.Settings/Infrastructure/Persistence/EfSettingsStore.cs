using Microsoft.EntityFrameworkCore;
using PlaceContext.Settings.Persistence;

namespace PlaceContext.Settings.Infrastructure.Persistence;

public sealed class EfSettingsStore(SettingsDbContext db) : ISettingsStore
{
    public Task<string?> GetBrandingAsync(Guid tenantId, CancellationToken ct = default)
        => db.Tenants.AsNoTracking().Where(row => row.Id == tenantId).Select(row => row.BrandingJson).FirstOrDefaultAsync(ct);

    public Task<string?> GetMenuAsync(Guid tenantId, CancellationToken ct = default)
        => db.Tenants.AsNoTracking().Where(row => row.Id == tenantId).Select(row => row.MenuJson).FirstOrDefaultAsync(ct);

    public Task<string?> GetArtifactViewAsync(Guid tenantId, CancellationToken ct = default)
        => db.Tenants.AsNoTracking().Where(row => row.Id == tenantId).Select(row => row.ArtifactViewJson).FirstOrDefaultAsync(ct);

    public Task<bool> IsDefaultAdminAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        => db.Users.AsNoTracking().AnyAsync(row => row.Id == userId && row.TenantId == tenantId && row.IsDefaultAdmin, ct);

    public Task SetBrandingAsync(Guid tenantId, string? json, CancellationToken ct = default)
        => UpdateTenantAsync(tenantId, row => row.BrandingJson = json, ct);

    public Task SetMenuAsync(Guid tenantId, string json, CancellationToken ct = default)
        => UpdateTenantAsync(tenantId, row => row.MenuJson = json, ct);

    public Task SetArtifactViewAsync(Guid tenantId, string json, CancellationToken ct = default)
        => UpdateTenantAsync(tenantId, row => row.ArtifactViewJson = json, ct);

    public Task SetTimeZoneAsync(Guid tenantId, string timeZoneId, CancellationToken ct = default)
        => UpdateTenantAsync(tenantId, row => row.TimeZoneId = timeZoneId, ct);

    private async Task UpdateTenantAsync(Guid tenantId, Action<SettingsTenantRow> update, CancellationToken ct)
    {
        var row = await db.Tenants.FirstOrDefaultAsync(candidate => candidate.Id == tenantId, ct);
        if (row is null) return;
        update(row);
        await db.SaveChangesAsync(ct);
    }
}
