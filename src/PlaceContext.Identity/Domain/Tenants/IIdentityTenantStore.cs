namespace PlaceContext.Identity.Domain.Tenants;

public interface IIdentityTenantStore
{
    Task<IdentityTenantDetails?> FindByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<IdentityTenantDetails?> FindBySlugAsync(string slug, CancellationToken ct = default);
    Task<IdentityTenantDetails?> FindByCustomerPortalDomainAsync(string domain, CancellationToken ct = default);
    Task<IdentityTenantDetails> GetOrCreateAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<IdentityTenantDetails>> ListAsync(int take = 1000, CancellationToken ct = default);
    Task SaveGitHubAsync(Guid tenantId, string githubLogin, string accessToken, CancellationToken ct = default);
    Task SetTimeZoneAsync(Guid tenantId, string timeZoneId, CancellationToken ct = default);
    Task SetCustomerPortalDomainAsync(Guid tenantId, string? domain, CancellationToken ct = default);
    Task SetCustomerPortalEnabledAsync(Guid tenantId, bool enabled, CancellationToken ct = default);
}
