using Microsoft.AspNetCore.Http;
using PlaceContext.Application.Ports;

namespace PlaceContext.ServiceDefaults;

/// <summary>
/// Resolves permissions from the JWT claims already validated by an independently hosted service.
/// The edge is responsible for issuing the claims; services remain responsible for enforcing them.
/// </summary>
public sealed class ServiceClaimPermissionService(IHttpContextAccessor httpContextAccessor)
    : IPermissionService
{
    public Task<bool> HasAsync(string permission, CancellationToken ct = default)
        => Task.FromResult(EffectivePermissions().Contains(permission));

    public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlySet<string>>(EffectivePermissions());

    public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(
        Guid userId,
        string roleName,
        CancellationToken ct = default)
    {
        _ = userId;
        _ = roleName;
        return GetEffectivePermissionsAsync(ct);
    }

    private HashSet<string> EffectivePermissions()
        => httpContextAccessor.HttpContext?.User
            .FindAll(ServiceAuthenticationDefaults.PermissionClaim)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
}
