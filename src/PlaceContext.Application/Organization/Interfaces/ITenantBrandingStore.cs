using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Organization;

public interface ITenantBrandingStore
{
    Task<TenantBranding> GetAsync(CancellationToken ct = default);
    // The command handler owns the commit through IUnitOfWork.
    Task SetAsync(TenantBranding branding, CancellationToken ct = default);
}
