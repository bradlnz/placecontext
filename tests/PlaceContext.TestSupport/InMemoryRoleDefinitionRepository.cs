using PlaceContext.Application.Access;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.TestSupport;

/// <summary>In-memory role-definition store, pre-seeded with the four system roles from
/// <see cref="RolePermissionDefaults"/> (mirroring the EF repository's lazy seed) so handler tests
/// start from the same baseline as a real tenant.</summary>
public sealed class InMemoryRoleDefinitionRepository : IRoleDefinitionRepository
{
    private readonly List<RoleDefinition> _roles;

    public InMemoryRoleDefinitionRepository()
        => _roles = Enum.GetValues<UserRole>()
            .Select(r => new RoleDefinition(
                Guid.NewGuid(), r.ToString(), IsSystem: true,
                RolePermissionDefaults.GetDefaults(r).OrderBy(p => p, StringComparer.Ordinal).ToList()))
            .ToList();

    public Task<IReadOnlyList<RoleDefinition>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RoleDefinition>>(_roles.ToList());

    public Task<RoleDefinition?> GetByNameAsync(string name, CancellationToken ct = default)
        => Task.FromResult(_roles.FirstOrDefault(r => r.Name == name));

    public Task<RoleDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_roles.FirstOrDefault(r => r.Id == id));

    public Task<RoleDefinition> CreateAsync(string name, IReadOnlyList<string> permissions, CancellationToken ct = default)
    {
        var created = new RoleDefinition(Guid.NewGuid(), name, IsSystem: false, permissions.ToList());
        _roles.Add(created);
        return Task.FromResult(created);
    }

    public Task SetPermissionsAsync(Guid id, IReadOnlyList<string> permissions, CancellationToken ct = default)
    {
        var index = _roles.FindIndex(r => r.Id == id);
        if (index >= 0) _roles[index] = _roles[index] with { Permissions = permissions.ToList() };
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _roles.RemoveAll(r => r.Id == id);
        return Task.CompletedTask;
    }
}
