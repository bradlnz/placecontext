using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Access;

/// <summary>Pure helper assembling the full-catalog <see cref="PermissionGrantView"/> matrix for one
/// member, from their role and their tenant-scoped overrides. Shared by the query and command handlers
/// so both return the same shape after a read or a write.</summary>
public static class PermissionMatrixBuilder
{
    public static IReadOnlyList<PermissionGrantView> Build(UserRole role, IReadOnlyDictionary<string, bool> overrides)
    {
        var defaults = RolePermissionDefaults.GetDefaults(role);
        var effective = EffectivePermissionsResolver.Resolve(role, overrides);
        return Permission.All
            .Select(p => new PermissionGrantView(
                p, defaults.Contains(p), overrides.TryGetValue(p, out var o) ? o : null, effective.Contains(p)))
            .ToList();
    }
}
