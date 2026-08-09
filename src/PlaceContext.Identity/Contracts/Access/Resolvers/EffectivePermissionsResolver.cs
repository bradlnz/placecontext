using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Access;

/// <summary>
/// Pure function combining a role's permission grant set with a member's explicit tenant-scoped
/// overrides: an allow override adds a permission the role wouldn't otherwise grant; a revoke override
/// removes one it would — an explicit revoke always wins, whether or not the role grant set already
/// included it. No I/O — safe to unit-test in isolation.
///
/// Defense in depth for the default-admin gate: <c>settings.manage</c> is never effective for anyone
/// but the tenant's default admin, regardless of what the role grants or an override allows — the
/// /settings/* area is restricted to the default admin (see <c>Policies.DefaultAdmin</c> in the Host).
/// </summary>
public static class EffectivePermissionsResolver
{
    public static IReadOnlySet<string> Resolve(
        IReadOnlySet<string> roleGrants, IReadOnlyDictionary<string, bool> overrides, bool isDefaultAdmin)
    {
        var effective = new HashSet<string>(roleGrants);
        foreach (var (permission, allowed) in overrides)
        {
            if (allowed) effective.Add(permission);
            else effective.Remove(permission); // revoke wins, regardless of the role grant
        }
        if (!isDefaultAdmin) effective.Remove(Permission.SettingsManage);
        return effective;
    }

    /// <summary>The raw role-default + override math with no default-admin gate applied — kept for
    /// callers that only exercise the pure combination logic (unit tests).</summary>
    public static IReadOnlySet<string> Resolve(UserRole role, IReadOnlyDictionary<string, bool> overrides)
        => Resolve(RolePermissionDefaults.GetDefaults(role), overrides, isDefaultAdmin: true);
}
