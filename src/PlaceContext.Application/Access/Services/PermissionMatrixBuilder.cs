using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Access;

/// <summary>Pure helper assembling the full-catalog <see cref="PermissionGrantView"/> matrix for one
/// member, from their role's grant set and their tenant-scoped overrides. Shared by the query and
/// command handlers so both return the same shape after a read or a write.</summary>
public static class PermissionMatrixBuilder
{
    public static IReadOnlyList<PermissionGrantView> Build(
        IReadOnlySet<string> roleGrants, IReadOnlyDictionary<string, bool> overrides, bool isDefaultAdmin)
    {
        var effective = EffectivePermissionsResolver.Resolve(roleGrants, overrides, isDefaultAdmin);
        return Permission.All
            .Select(p => new PermissionGrantView(
                p, roleGrants.Contains(p), overrides.TryGetValue(p, out var o) ? o : null, effective.Contains(p)))
            .ToList();
    }
}
