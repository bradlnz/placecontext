using PlaceContext.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Tenancy;

/// <summary>Registry of tenants, keyed by subdomain slug. Not itself tenant-scoped.</summary>
public interface ITenantStore
{
    Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct = default);
    /// <summary>Resolves the tenant for a slug, provisioning one on first sight (self-service signup).</summary>
    Task<TenantInfo> GetOrCreateAsync(string slug, CancellationToken ct = default);
    Task<TenantRow?> GetRowAsync(Guid tenantId, CancellationToken ct = default);
    Task SaveGitHubAsync(Guid tenantId, string githubLogin, string accessToken, CancellationToken ct = default);
    Task SetTimeZoneAsync(Guid tenantId, string timeZoneId, CancellationToken ct = default);
}
