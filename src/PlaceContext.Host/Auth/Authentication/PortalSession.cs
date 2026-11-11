using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using PlaceContext.Application.Membership;
using PlaceContext.Application.Tenancy;

namespace PlaceContext.Host.Auth;

internal static class PortalSession
{
    public static async Task ValidateCookieAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var oldRole = context.Principal?.FindFirstValue(ClaimTypes.Role);
        if (context.Principal is null || !await ValidateAsync(context.Principal,
            services.GetRequiredService<ICurrentTenant>(), services.GetRequiredService<IMembershipService>(),
            context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            return;
        }
        context.ShouldRenew = oldRole != context.Principal.FindFirstValue(ClaimTypes.Role);
    }

    public static async Task<bool> ValidateAsync(ClaimsPrincipal principal, ICurrentTenant tenant,
        IMembershipService members, CancellationToken ct = default)
    {
        if (!tenant.IsResolved || tenant.TenantId == Guid.Empty
            || !Guid.TryParse(principal.FindFirstValue("tenant"), out var tenantId) || tenantId != tenant.TenantId
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
            return false;
        var role = await members.GetRoleAsync(userId, ct);
        if (string.IsNullOrWhiteSpace(role)) return false;
        foreach (var claim in identity.FindAll(ClaimTypes.Role).Concat(identity.FindAll("role")).ToArray())
            identity.RemoveClaim(claim);
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim("role", role));
        return true;
    }
}
