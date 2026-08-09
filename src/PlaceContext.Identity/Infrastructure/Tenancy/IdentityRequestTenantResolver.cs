using PlaceContext.Application.Ports;
using PlaceContext.Identity.Domain.Tenants;

namespace PlaceContext.Identity.Infrastructure.Tenancy;

public sealed class IdentityRequestTenantResolver(IIdentityTenantStore tenants) : IRequestTenantResolver
{
    private static readonly string[] TenantBaseDomains = [".placecontext.ai", ".localhost"];

    public async Task<TenantContext?> ResolveAsync(string host, CancellationToken ct = default)
    {
        host = NormalizeHost(host);
        var tenant = await tenants.FindByCustomerPortalDomainAsync(host, ct)
            ?? await tenants.GetOrCreateAsync(ResolveSlug(host), ct);
        return new TenantContext(tenant.Id, tenant.Slug, tenant.TimeZoneId);
    }

    private static string NormalizeHost(string host)
    {
        host = (host ?? string.Empty).Trim().ToLowerInvariant();
        if (host.StartsWith('['))
        {
            var closingBracket = host.IndexOf(']');
            return closingBracket >= 0 ? host[..(closingBracket + 1)] : host;
        }
        var colon = host.LastIndexOf(':');
        return colon > 0 ? host[..colon] : host;
    }

    private static string ResolveSlug(string host)
    {
        foreach (var baseDomain in TenantBaseDomains)
        {
            if (!host.EndsWith(baseDomain, StringComparison.Ordinal)) continue;
            var subdomain = host[..^baseDomain.Length];
            return string.IsNullOrEmpty(subdomain) ? "default" : subdomain.Split('.')[0];
        }
        return "default";
    }
}
