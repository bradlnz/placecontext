using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Access;

/// <summary>
/// Pure function combining a role's default permissions with a member's explicit tenant-scoped
/// overrides: an allow override adds a permission the role wouldn't otherwise grant; a revoke override
/// removes one it would — an explicit revoke always wins, whether or not the role default already
/// included it. No I/O — safe to unit-test in isolation.
/// </summary>
public static class EffectivePermissionsResolver
{
    public static IReadOnlySet<string> Resolve(UserRole role, IReadOnlyDictionary<string, bool> overrides)
    {
        var effective = new HashSet<string>(RolePermissionDefaults.GetDefaults(role));
        foreach (var (permission, allowed) in overrides)
        {
            if (allowed) effective.Add(permission);
            else effective.Remove(permission); // revoke wins, regardless of the role default
        }
        return effective;
    }
}
