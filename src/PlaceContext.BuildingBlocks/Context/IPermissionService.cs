namespace PlaceContext.Application.Ports;

public interface IPermissionService
{
    Task<bool> HasAsync(string permission, CancellationToken ct = default);
    Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default);
    Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(
        Guid userId,
        string roleName,
        CancellationToken ct = default);
}
