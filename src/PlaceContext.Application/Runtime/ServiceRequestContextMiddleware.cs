using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Runtime;

/// <summary>
/// Copies trusted JWT claims into the ambient request context used by tenant-scoped repositories.
/// The context is always cleared when the request completes to prevent AsyncLocal leakage.
/// </summary>
public sealed class ServiceRequestContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        ICurrentTenantAccessor tenantAccessor,
        ICurrentUserAccessor userAccessor)
    {
        tenantAccessor.Clear();
        userAccessor.Clear();

        try
        {
            if (httpContext.User.Identity?.IsAuthenticated == true)
            {
                SetTenant(httpContext.User, tenantAccessor);
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

    private static void SetTenant(ClaimsPrincipal principal, ICurrentTenantAccessor accessor)
    {
        if (!Guid.TryParse(principal.FindFirst("tenant")?.Value, out var tenantId))
            return;

        var slug = principal.FindFirst("tenant_slug")?.Value;
        var timeZone = principal.FindFirst("tenant_timezone")?.Value;
        accessor.Set(new TenantContext(
            tenantId,
            string.IsNullOrWhiteSpace(slug) ? tenantId.ToString("N") : slug,
            string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone));
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
