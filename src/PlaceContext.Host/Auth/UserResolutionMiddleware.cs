using System.Security.Claims;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Auth;

/// <summary>
/// Resolves the ambient <see cref="CurrentUser"/> (id + role) from the request's authenticated
/// principal. Placed AFTER <c>UseAuthorization</c> deliberately: the authorization middleware
/// re-authenticates against an endpoint's specified schemes (e.g. the MCP endpoint's bearer scheme) and
/// replaces <c>HttpContext.User</c> with the scheme-correct result before evaluating requirements, so
/// reading the principal any earlier in the pipeline would see the wrong (or no) identity for bearer
/// requests. By this point in the pipeline the request has already been authorized, so this only needs
/// to capture identity — not gate anything.
///
/// Also seeds the scoped <see cref="UserHolder"/> so a Blazor circuit's later inbound activities (which
/// don't share this request's AsyncLocal flow) can re-apply the same identity — see
/// <see cref="UserCircuitHandler"/>, which mirrors how <c>TenantCircuitHandler</c> solves the identical
/// problem for the ambient tenant.
/// </summary>
public sealed class UserResolutionMiddleware
{
    private readonly RequestDelegate _next;
    public UserResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context, UserHolder holder)
    {
        var user = Resolve(context.User);
        if (user is not null)
        {
            CurrentUser.Set(user);
            holder.User = user;
        }
        await _next(context);
    }

    private static UserIdentity? Resolve(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) return null;
        var idClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
        var roleClaim = principal.FindFirst(ClaimTypes.Role)?.Value ?? principal.FindFirst("role")?.Value;
        if (!Guid.TryParse(idClaim, out var id) || !Enum.TryParse<UserRole>(roleClaim, out var role)) return null;
        return new UserIdentity(id, role);
    }
}
