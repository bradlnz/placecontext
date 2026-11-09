using PlaceContext.Application.Access.Models;
namespace PlaceContext.Infrastructure.Tests.AgentWorkflows;

internal sealed class AgentTestPermissions : IPermissionService
{
    public HashSet<string> Denied { get; } = [];
    public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(!Denied.Contains(permission));
    public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<string>>(Permission.All.Where(x => !Denied.Contains(x)).ToHashSet());
    public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, string roleName, CancellationToken ct = default) => GetEffectivePermissionsAsync(ct);
}
