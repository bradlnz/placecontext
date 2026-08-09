using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PlaceContext.Application.Ports;

namespace PlaceContext.Identity.Auth;

public sealed class DefaultAdminAuthorizationHandler(IMembershipService membership)
    : AuthorizationHandler<DefaultAdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DefaultAdminRequirement requirement)
    {
        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");
        if (Guid.TryParse(value, out var userId)
            && await membership.IsDefaultAdminAsync(userId))
            context.Succeed(requirement);
    }
}
