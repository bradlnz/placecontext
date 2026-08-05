using Microsoft.AspNetCore.Authorization;

namespace PlaceContext.Host.Auth;

/// <summary>Validates machine-client scope requirements for Core API callers.</summary>
public sealed class CoreApiScopeAuthorizationHandler : AuthorizationHandler<CoreApiScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CoreApiScopeRequirement requirement)
    {
        var hasScope = context.User
            .FindAll("scope")
            .Any(c => string.Equals(c.Value, requirement.Scope, StringComparison.OrdinalIgnoreCase));

        if (!hasScope)
        {
            // OAuth/jwt-compatible handlers often use `scp` with space-separated values.
            hasScope = context.User.Claims
                .Where(c => c.Type == "scp")
                .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Any(scope => string.Equals(scope, requirement.Scope, StringComparison.OrdinalIgnoreCase));
        }

        if (hasScope)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
