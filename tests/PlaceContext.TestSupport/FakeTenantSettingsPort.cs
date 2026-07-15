using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

/// <summary>In-memory <see cref="ITenantSettingsPort"/> — a single settings row per tenant id.</summary>
public sealed class FakeTenantSettingsPort : ITenantSettingsPort
{
    private readonly Dictionary<Guid, TenantSettingsSnapshot> _store = new();

    public Task<TenantSettingsSnapshot> GetAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult(_store.TryGetValue(tenantId, out var s) ? s : new TenantSettingsSnapshot("UTC", null, null));

    public Task ApplyAsync(Guid tenantId, TenantSettingsSnapshot snapshot, CancellationToken ct = default)
    {
        var current = _store.TryGetValue(tenantId, out var s) ? s : new TenantSettingsSnapshot("UTC", null, null);
        _store[tenantId] = new TenantSettingsSnapshot(
            string.IsNullOrWhiteSpace(snapshot.TimeZoneId) ? current.TimeZoneId : snapshot.TimeZoneId,
            snapshot.BrandingJson ?? current.BrandingJson,
            string.IsNullOrWhiteSpace(snapshot.GitHubLogin) ? current.GitHubLogin : snapshot.GitHubLogin);
        return Task.CompletedTask;
    }
}
