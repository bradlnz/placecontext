using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

public sealed class FakeTenantCatalog : ITenantCatalog
{
    private readonly IReadOnlyList<TenantContext> _tenants;

    public FakeTenantCatalog(params TenantContext[] tenants) => _tenants = tenants;

    public Task<IReadOnlyList<TenantContext>> ListAsync(CancellationToken ct = default)
        => Task.FromResult(_tenants);

    public Task<TenantContext?> FindAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult(_tenants.FirstOrDefault(tenant => tenant.Id == tenantId));
}
