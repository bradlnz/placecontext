using Microsoft.AspNetCore.Http;
using PlaceContext.Application.Ports;

namespace PlaceContext.Identity.Infrastructure;

/// <summary>Resolves anonymous setup/login requests from the public host before identity storage runs.</summary>
public sealed class IdentityTenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ICurrentTenant currentTenant,
        ICurrentTenantAccessor tenantAccessor,
        IRequestTenantResolver tenantResolver)
    {
        if (!currentTenant.IsResolved)
        {
            var forwardedHost = context.Request.Headers["X-Forwarded-Host"]
                .ToString().Split(',')[0].Trim();
            var host = string.IsNullOrWhiteSpace(forwardedHost)
                ? context.Request.Host.Value ?? string.Empty
                : forwardedHost;
            var tenant = await tenantResolver.ResolveAsync(host, context.RequestAborted);
            if (tenant is not null)
                tenantAccessor.Set(tenant);
        }

        await next(context);
    }
}
