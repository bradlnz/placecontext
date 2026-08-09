using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PlaceContext.Application.Ports;

namespace PlaceContext.ServiceDefaults;

/// <summary>
/// Copies trusted JWT claims into the ambient request context used by tenant-scoped repositories.
/// The context is always cleared when the request completes to prevent AsyncLocal leakage.
/// </summary>
public sealed class ServiceRequestContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        ICurrentTenantAccessor tenantAccessor,
        ICurrentUserAccessor userAccessor,
        IRequestTenantResolver tenantResolver)
    {
        tenantAccessor.Clear();
        userAccessor.Clear();

        try
        {
            if (httpContext.User.Identity?.IsAuthenticated == true)
            {
                if (!SetTenant(httpContext.User, tenantAccessor)
                    && string.Equals(
                        httpContext.User.Identity.AuthenticationType,
                        ServiceApiKeyAuthenticationDefaults.Scheme,
                        StringComparison.Ordinal))
                {
                    var forwardedHost = httpContext.Request.Headers["X-Forwarded-Host"]
                        .ToString().Split(',')[0].Trim();
                    var requestHost = string.IsNullOrWhiteSpace(forwardedHost)
                        ? httpContext.Request.Host.Value ?? string.Empty
                        : forwardedHost;
                    var tenant = await tenantResolver.ResolveAsync(
                        requestHost,
                        httpContext.RequestAborted);
                    if (tenant is null)
                    {
                        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await httpContext.Response.WriteAsJsonAsync(
                            new { error = "The workspace tenant resolver is not configured." },
                            httpContext.RequestAborted);
                        return;
                    }

                    tenantAccessor.Set(tenant);
                }
                SetUser(httpContext.User, userAccessor);
            }

            await next(httpContext);
        }
        finally
        {
            userAccessor.Clear();
            tenantAccessor.Clear();
        }
    }

    private static bool SetTenant(ClaimsPrincipal principal, ICurrentTenantAccessor accessor)
    {
        if (!Guid.TryParse(principal.FindFirst("tenant")?.Value, out var tenantId))
            return false;

        var slug = principal.FindFirst("tenant_slug")?.Value;
        var timeZone = principal.FindFirst("tenant_timezone")?.Value;
        accessor.Set(new TenantContext(
            tenantId,
            string.IsNullOrWhiteSpace(slug) ? tenantId.ToString("N") : slug,
            string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone));
        return true;
    }

    private static void SetUser(ClaimsPrincipal principal, ICurrentUserAccessor accessor)
    {
        var idClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;
        if (!Guid.TryParse(idClaim, out var userId))
            return;

        var role = principal.FindFirst(ClaimTypes.Role)?.Value
            ?? principal.FindFirst("role")?.Value
            ?? nameof(UserRole.Viewer);
        accessor.Set(new UserContext(userId, role));
    }
}
