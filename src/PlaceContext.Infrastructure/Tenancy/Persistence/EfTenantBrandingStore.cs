using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Organization;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Tenancy;

public sealed class EfTenantBrandingStore : ITenantBrandingStore
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _tenant;

    public EfTenantBrandingStore(AppDbContext db, ICurrentTenant tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<TenantBranding> GetAsync(CancellationToken ct = default)
    {
        if (!_tenant.IsResolved)
        {
            return new TenantBranding();
        }
        var json = await _db.Tenants.AsNoTracking()
                    .Where(t => t.Id == _tenant.TenantId)
                    .Select(t => t.BrandingJson)
                    .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new TenantBranding();
        }
        try
        {
            return JsonSerializer.Deserialize<TenantBranding>(json, Json) ?? new TenantBranding();
        }
        catch
        {
            return new TenantBranding();
        }
    }

    public async Task SetAsync(TenantBranding branding, CancellationToken ct = default)
    {
        if (!_tenant.IsResolved)
        {
            return;
        }
        var row = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct);
        if (row is null)
        {
            return;
        }
        row.BrandingJson = branding.IsDefault ? null : JsonSerializer.Serialize(branding, Json);
    }
}
