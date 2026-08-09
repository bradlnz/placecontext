namespace PlaceContext.Application.Ports;

/// <summary>Read-only directory used by cross-tenant service workers.</summary>
public interface ITenantCatalog
{
    Task<IReadOnlyList<TenantContext>> ListAsync(CancellationToken ct = default);
    Task<TenantContext?> FindAsync(Guid tenantId, CancellationToken ct = default);
}
