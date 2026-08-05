using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.CustomerPortal;

/// <summary>
/// Binds one independently deployed portal to exactly one existing tenant and one public host.
/// Unlike the operator Host, this service never creates a tenant from an arbitrary Host header.
/// </summary>
public sealed class CustomerPortalTenantMiddleware
{
    private readonly RequestDelegate _next;
    public CustomerPortalTenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        IOptions<CustomerPortalOptions> options,
        ITenantStore tenants)
    {
        var configured = options.Value;
        if (context.Request.Path.StartsWithSegments("/healthz"))
        {
            await _next(context);
            return;
        }
        var host = context.Request.Host.Host.TrimEnd('.');
        if (!string.Equals(host, configured.Domain.Trim().TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
            return;
        }

        var row = await tenants.GetRowAsync(configured.TenantId, context.RequestAborted);
        if (row is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("Portal tenant is not provisioned.", context.RequestAborted);
            return;
        }

        var tenant = new TenantInfo(row.Id, row.Slug, row.Name, row.TimeZoneId);
        CurrentTenant.Set(tenant);
        try { await _next(context); }
        finally { CurrentTenant.Clear(); }
    }
}
