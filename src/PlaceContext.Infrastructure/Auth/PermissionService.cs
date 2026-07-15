using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Access;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.Auth;

/// <summary>
/// Resolves effective permissions: a role's defaults (<see cref="RolePermissionDefaults"/>) with a
/// user's tenant-scoped overrides applied (<see cref="EffectivePermissionsResolver"/>). Backs both the
/// ambient "current caller" checks (Blazor pages, the dispatcher's <c>IRequiresPermission</c> gate) and
/// the arbitrary-user resolution the per-permission authorization policy handler and the Access
/// Settings admin UI need.
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

    public async Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, UserRole role, CancellationToken ct = default)
    {
        // Permission checks fire from the authorization pipeline (AuthorizeRouteView/AuthorizeView) and
        // from pages concurrently with their own data loads — all on the same Blazor circuit's scoped
        // AppDbContext. Reading grants on that shared context races the render ("a second operation was
        // started on this context instance"). Resolve the repo in an isolated scope (its own short-lived
        // AppDbContext) so permission reads never contend with the circuit's context. The ambient tenant
        // (AsyncLocal CurrentTenant) still flows into the new scope, so the query stays tenant-scoped.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IUserPermissionGrantRepository>();
        var overrides = await grants.ListForUserAsync(userId, ct);
        return EffectivePermissionsResolver.Resolve(role, overrides);
    }
}
