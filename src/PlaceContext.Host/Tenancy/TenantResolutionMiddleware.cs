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
        var host = context.Request.Host.Host;
        TenantInfo? customDomainTenant = null;
        if (context.Request.Path.StartsWithSegments("/api/customer-portal"))
        {
            if (!Guid.TryParse(context.Request.Headers["X-PlaceContext-Tenant-Id"], out var apiTenantId))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("X-PlaceContext-Tenant-Id is required.");
                return;
            }

            var row = await store.GetRowAsync(apiTenantId, context.RequestAborted);
            if (row is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!row.CustomerPortalEnabled)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Customer portal accounts are disabled for this tenant.");
                return;
            }

            customDomainTenant = new TenantInfo(row.Id, row.Slug, row.Name, row.TimeZoneId);
        }
        else
        {
            customDomainTenant = await store.FindByCustomerPortalDomainAsync(host, context.RequestAborted);
        }

        var tenant = customDomainTenant ?? await store.GetOrCreateAsync(ResolveSlug(host), context.RequestAborted);
        if (customDomainTenant is not null)
        {
            context.Items["PlaceContext.CustomerPortalDomain"] = true;
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
