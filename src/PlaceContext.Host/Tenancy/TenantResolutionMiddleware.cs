using Microsoft.AspNetCore.Components.Server.Circuits;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Tenancy;

/// <summary>
/// Resolves the tenant from the request's host — the <c>{user}.placecontext.ai</c> subdomain (or
/// <c>{user}.localhost</c> in dev) — provisioning one on first sight. Sets the ambient
/// <see cref="CurrentTenant"/> (covers MCP + the prerender pass) and the scoped <see cref="TenantHolder"/>
/// (which the circuit handler reads to keep interactive Blazor renders tenant-scoped).
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

    /// <summary>Extracts the tenant slug from a host name. Bare/apex hosts map to the "default" tenant.</summary>
    public static string ResolveSlug(string host)
    {
        host = (host ?? string.Empty).ToLowerInvariant();
        foreach (var baseDomain in new[] { ".placecontext.ai", ".localhost" })
            if (host.EndsWith(baseDomain, StringComparison.Ordinal))
            {
                var sub = host[..^baseDomain.Length];
                return string.IsNullOrEmpty(sub) ? "default" : sub.Split('.')[0];
            }

        if (host is "localhost" or "placecontext.ai" or "127.0.0.1" || host.Length == 0)
            return "default";

        var parts = host.Split('.');
        return parts.Length >= 3 ? parts[0] : "default"; // sub.example.com → "sub"
    }
}
