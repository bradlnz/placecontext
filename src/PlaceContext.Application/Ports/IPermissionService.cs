namespace PlaceContext.Application.Ports;

/// <summary>
/// Resolves effective permissions — a role's defaults with a member's tenant-scoped overrides applied
/// (see <c>PlaceContext.Application.Access</c> for the pure resolution rules). Backs three call sites:
/// the ASP.NET Core per-permission authorization policies (both cookie and bearer requests), the
/// dispatcher's <c>IRequiresPermission</c> check on sensitive commands, and the Blazor pages that hide
/// an action a caller can't perform (defense-in-depth only — the dispatcher check is what actually
/// blocks the action server-side).
/// </summary>
public interface IPermissionService
{
    /// <summary>Whether the current caller (<see cref="ICurrentUser"/>) holds the given permission.</summary>
    Task<bool> HasAsync(string permission, CancellationToken ct = default);

    /// <summary>The current caller's full effective permission set.</summary>
    Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default);

    /// <summary>The effective permission set for an arbitrary member (role + overrides), independent of
    /// who is asking — used by the Access Settings admin UI and the authorization policy handler, which
    /// both already know the target user's id and role from elsewhere (a claim, a member row) without
    /// needing them to be the ambient caller.</summary>
    Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, UserRole role, CancellationToken ct = default);
}
