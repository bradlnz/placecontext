using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Organization;
namespace PlaceContext.Application.Features;

public sealed class GetTenantBrandingHandler(ITenantBrandingStore store)
    : IQueryHandler<GetTenantBrandingQuery, TenantBranding>
{
    public Task<TenantBranding> HandleAsync(GetTenantBrandingQuery query, CancellationToken ct = default)
        => store.GetAsync(ct);
}
