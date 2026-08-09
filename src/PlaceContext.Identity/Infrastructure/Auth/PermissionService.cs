using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Access;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Identity.Infrastructure.Persistence;

namespace PlaceContext.Identity.Infrastructure.Auth;

/// <summary>
/// Resolves effective permissions: a role's grant set (role_definitions, falling back to
/// <see cref="RolePermissionDefaults"/>) with a user's tenant-scoped overrides applied
/// (<see cref="EffectivePermissionsResolver"/>), hardened so <c>settings.manage</c> is never effective
/// for anyone but the tenant's default admin. Backs both the ambient "current caller" checks (Blazor
/// pages, the dispatcher's <c>IRequiresPermission</c> gate) and the arbitrary-user resolution the
/// per-permission authorization policy handler and the Access Settings admin UI need.
/// </summary>
public sealed class PermissionService : IPermissionService
{
    private readonly ICurrentUser _currentUser;
    private readonly IServiceScopeFactory _scopeFactory;

    public PermissionService(ICurrentUser currentUser, IServiceScopeFactory scopeFactory)
        => (_currentUser, _scopeFactory) = (currentUser, scopeFactory);

    public async Task<bool> HasAsync(string permission, CancellationToken ct = default)
        => (await GetEffectivePermissionsAsync(ct)).Contains(permission);

    public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default)
        => _currentUser.IsAuthenticated
            ? GetEffectivePermissionsForUserAsync(_currentUser.UserId, _currentUser.Role, ct)
            : Task.FromResult<IReadOnlySet<string>>(new HashSet<string>()); // unauthenticated → deny-by-default

    public async Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, string roleName, CancellationToken ct = default)
    {
        // Permission checks fire from the authorization pipeline (AuthorizeRouteView/AuthorizeView) and
        // from pages concurrently with their own data loads — all on the same Blazor circuit's scoped
        // IdentityDbContext. Reading grants on that shared context races the render ("a second operation was
        // started on this context instance"). Resolve the repos in an isolated scope (its own short-lived
        // IdentityDbContext) so permission reads never contend with the circuit's context. The ambient tenant
        // (AsyncLocal CurrentTenant) still flows into the new scope, so the queries stay tenant-scoped.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IUserPermissionGrantRepository>();
        var roleDefinitions = scope.ServiceProvider.GetRequiredService<IRoleDefinitionRepository>();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var overrides = await grants.ListForUserAsync(userId, ct);
        var isDefaultAdmin = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.IsDefaultAdmin)
            .FirstOrDefaultAsync(ct);
        var definition = await roleDefinitions.GetByNameAsync(roleName, ct);
        IReadOnlySet<string> roleGrants = definition is not null
            ? new HashSet<string>(definition.Permissions, StringComparer.Ordinal)
            : Enum.TryParse<UserRole>(roleName, out var parsed)
                ? RolePermissionDefaults.GetDefaults(parsed)
                : new HashSet<string>(); // unknown role name — deny-by-default
        return EffectivePermissionsResolver.Resolve(roleGrants, overrides, isDefaultAdmin);
    }
}
