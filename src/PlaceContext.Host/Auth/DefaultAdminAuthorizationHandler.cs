using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Host.Auth;

/// <summary>
/// Backs the <see cref="Policies.DefaultAdmin"/> policy. Reads the requesting user's id off
/// <see cref="AuthorizationHandlerContext.User"/> — already the scheme-correct principal by the time
/// requirements run (mirroring <see cref="PermissionAuthorizationHandler"/>) — then checks
/// <c>IsDefaultAdmin</c> on their user row. The user lookup runs in an isolated DI scope (its own
/// short-lived AppDbContext) for the same reason <c>PermissionService</c> does: policy evaluation can
/// fire on a Blazor circuit concurrently with the page's own data loads on the circuit-shared scoped
/// context, and the ambient tenant (AsyncLocal) still flows into the new scope so the query stays
/// tenant-scoped. Deny-by-default: a missing/unparsable claim or a non-flagged row fails closed.
/// </summary>
public sealed class DefaultAdminAuthorizationHandler : AuthorizationHandler<DefaultAdminRequirement>
{
    private readonly IServiceScopeFactory _scopeFactory;
    public DefaultAdminAuthorizationHandler(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, DefaultAdminRequirement requirement)
    {
        var idClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? context.User.FindFirst("sub")?.Value;
        var tenantClaim = context.User.FindFirst("tenant")?.Value;
        if (!Guid.TryParse(idClaim, out var userId) || !Guid.TryParse(tenantClaim, out var tenantId))
            return; // no parseable identity — deny

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Authorization runs before UserResolutionMiddleware and may execute in a child DI scope.
        // Bind the lookup to the signed tenant claim explicitly instead of depending on ambient state.
        var isDefaultAdmin = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == tenantId)
            .Select(u => u.IsDefaultAdmin)
            .FirstOrDefaultAsync();
        if (isDefaultAdmin)
            context.Succeed(requirement);
    }
}
