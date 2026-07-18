using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Tenancy;

/// <summary>
/// Resolves the tenant from the request's host — the <c>{user}.placecontext.ai</c> subdomain (or
/// <c>{user}.localhost</c> in dev) — provisioning one on first sight for those known suffixes only.
/// Arbitrary Host headers no longer mint tenants (was: <c>foo.evil.com</c> → slug <c>foo</c>).
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context, ITenantStore store, TenantHolder holder)
    {
        var slug = ResolveSlug(context.Request.Host.Host);
        var tenant = await store.GetOrCreateAsync(slug, context.RequestAborted);
        CurrentTenant.Set(tenant);
        holder.Tenant = tenant;
        await _next(context);
    }

    /// <summary>
    /// Extracts the tenant slug from a host name. Only known product/dev bases yield a subdomain
    /// slug; everything else maps to the shared <c>default</c> tenant so Host-header injection
    /// cannot auto-provision attacker-chosen workspaces.
    /// </summary>
    public static string ResolveSlug(string host)
    {
        host = (host ?? string.Empty).ToLowerInvariant();
        foreach (var baseDomain in PublicUrl.TenantBaseDomains)
            if (host.EndsWith(baseDomain, StringComparison.Ordinal))
            {
                var sub = host[..^baseDomain.Length];
                return string.IsNullOrEmpty(sub) ? "default" : sub.Split('.')[0];
            }

        if (host.Length == 0 || PublicUrl.DefaultTenantHosts.Contains(host, StringComparer.Ordinal))
            return "default";

        // Unknown host (e.g. evil.attacker, foo.evil.com) → default, do not derive a slug.
        return "default";
    }
}
