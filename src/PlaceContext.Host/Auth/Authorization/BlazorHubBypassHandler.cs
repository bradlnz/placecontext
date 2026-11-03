using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PlaceContext.Host.Auth;

/// <summary>Succeeds for requests to Blazor SignalR hub endpoints (/_blazor/*) so the
/// FallbackPolicy's RequireAuthenticatedUser does not block Blazor initialisation on
/// public pages like /login and /setup.</summary>
public sealed class BlazorHubBypassHandler : AuthorizationHandler<BlazorHubBypass>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, BlazorHubBypass requirement)
    {
        if (context.Resource is AuthorizationFilterContext fctx &&
            fctx.HttpContext.Request.Path.StartsWithSegments("/_blazor"))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
