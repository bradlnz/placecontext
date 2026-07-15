using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Auth;

/// <summary>
/// Backs every per-permission policy. Reads the requesting user's id + role directly off
/// <see cref="AuthorizationHandlerContext.User"/> — already the scheme-correct principal for either the
/// portal cookie or the MCP bearer token by the time authorization requirements run, so there is no
/// need (and no ordering guarantee) to reach for the ambient <c>CurrentUser</c> here — then resolves
/// their effective permission set (role default + tenant-scoped overrides) and succeeds only if it
/// contains the required permission. Deny-by-default: any missing or unparsable claim fails closed
/// (the method simply returns without calling <see cref="AuthorizationHandlerContext.Succeed"/>).
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissions;
    public PermissionAuthorizationHandler(IPermissionService permissions) => _permissions = permissions;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var idClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? context.User.FindFirst("sub")?.Value;
        var roleClaim = context.User.FindFirst(ClaimTypes.Role)?.Value ?? context.User.FindFirst("role")?.Value;
        if (!Guid.TryParse(idClaim, out var userId) || !Enum.TryParse<UserRole>(roleClaim, out var role))
            return; // no parseable identity/role — deny

        var effective = await _permissions.GetEffectivePermissionsForUserAsync(userId, role);
        if (effective.Contains(requirement.Permission))
            context.Succeed(requirement);
    }
}
