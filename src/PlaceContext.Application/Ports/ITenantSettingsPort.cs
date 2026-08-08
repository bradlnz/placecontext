namespace PlaceContext.Application.Ports;

/// <summary>
/// Read/write access to a tenant's portable settings — the subset of the tenant registry row that is
/// safe to carry across a backup/restore (timezone, branding, GitHub login). Deliberately excludes
/// <c>GitHubToken</c>: it's a live OAuth secret, not configuration, and never leaves this deployment.
/// </summary>
public interface ITenantSettingsPort
{
    Task<TenantSettingsSnapshot> GetAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Applies non-null/non-empty fields of <paramref name="snapshot"/> onto the tenant row.
    /// A null or blank field leaves the existing value untouched (so a partial manifest can't blank
    /// out settings it didn't capture).</summary>
    Task ApplyAsync(Guid tenantId, TenantSettingsSnapshot snapshot, CancellationToken ct = default);
}
