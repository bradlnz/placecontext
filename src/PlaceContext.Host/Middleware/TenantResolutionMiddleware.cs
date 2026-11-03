using PlaceContext.Application.Tenancy;

namespace PlaceContext.Host.Tenancy;

/// <summary>
/// Resolves the tenant from the request's host — the <c>{user}.placecontext.io</c> subdomain (or
/// <c>{user}.localhost</c> in dev) — provisioning one on first sight for those known suffixes only.
/// Arbitrary Host headers no longer mint tenants (was: <c>foo.evil.com</c> → slug <c>foo</c>).
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context, ITenantStore store, TenantHolder holder,
        IConfiguration configuration)
    {
        // Login and registration on the canonical SaaS apex are public even
        // before the first tenant exists. Never bypass tenant lookup for other
        // hosts or tenant subdomains.
        if (configuration.GetValue<bool>("PlaceContext:SaaS:ManagedHosting")
            && ManagedSignupUrls.IsApexHost(context.Request.Host.Host, configuration))
        {
            // A newly installed managed apex has no tenant by design. The
            // homepage must lead to workspace discovery rather than a 404.
            if (context.Request.Path == "/")
            {
                context.Response.Redirect("/login");
                return;
            }
            if (ManagedSignupUrls.IsPublicApexPath(context.Request.Path))
            {
                await _next(context);
                return;
            }
        }
        var slug = ResolveSlug(context.Request.Host.Host);
        // Public hosted SaaS must never allocate a fresh tenant simply because a visitor
        // typed a new subdomain. Tenant creation is a privileged onboarding operation.
        var tenant = configuration.GetValue<bool>("PlaceContext:SaaS:RequireExplicitTenantProvisioning")
            ? await store.FindBySlugAsync(slug, context.RequestAborted)
            : await store.GetOrCreateAsync(slug, context.RequestAborted);
        if (tenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
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
        {
            if (host.EndsWith(baseDomain, StringComparison.Ordinal))
            {
                var sub = host[..^baseDomain.Length];
                return string.IsNullOrEmpty(sub) ? "default" : sub.Split('.')[0];
            }
        }
        if (host.Length == 0 || PublicUrl.DefaultTenantHosts.Contains(host, StringComparer.Ordinal))
        {
            return "default";
        }
        // Unknown host (e.g. evil.attacker, foo.evil.com) → default, do not derive a slug.
        return "default";
    }
}
