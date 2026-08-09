using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PlaceContext.Application.Ports;
using PlaceContext.Settings.Persistence;

namespace PlaceContext.Host.Auth;

public sealed class DefaultAdminAuthorizationHandler(
    ISettingsStore store,
    ICurrentTenant tenant) : AuthorizationHandler<DefaultAdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DefaultAdminRequirement requirement)
    {
        var idClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        if (!tenant.IsResolved || !Guid.TryParse(idClaim, out var userId)) return;
        if (await store.IsDefaultAdminAsync(tenant.TenantId, userId)) context.Succeed(requirement);
    }
}
