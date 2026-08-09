using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Settings.Persistence;

namespace PlaceContext.Host.Branding;

public sealed class BrandingService(ISettingsStore store, ICurrentTenant tenant)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public async Task<TenantBranding> GetAsync(CancellationToken ct = default)
    {
        if (!tenant.IsResolved) return new TenantBranding();
        var json = await store.GetBrandingAsync(tenant.TenantId, ct);
        if (string.IsNullOrWhiteSpace(json)) return new TenantBranding();
        try { return JsonSerializer.Deserialize<TenantBranding>(json, Json) ?? new TenantBranding(); }
        catch (JsonException) { return new TenantBranding(); }
    }

    public Task SetAsync(TenantBranding branding, CancellationToken ct = default)
        => tenant.IsResolved
            ? store.SetBrandingAsync(tenant.TenantId,
                branding.IsDefault ? null : JsonSerializer.Serialize(branding, Json), ct)
            : Task.CompletedTask;
}
