using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Access;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfRoleDefinitionRepository : IRoleDefinitionRepository
{
    private readonly AppDbContext _db;
    public EfRoleDefinitionRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<RoleDefinition>> ListAsync(CancellationToken ct = default)
    {
        await EnsureSystemRolesAsync(ct);
        var rows = await _db.RoleDefinitions.AsNoTracking()
            .OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)
            .ToListAsync(ct);
        return rows.Select(ToDefinition).ToList();
    }

    public async Task<RoleDefinition?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        await EnsureSystemRolesAsync(ct);
        var row = await _db.RoleDefinitions.AsNoTracking().FirstOrDefaultAsync(r => r.Name == name, ct);
        return row is null ? null : ToDefinition(row);
    }

    public async Task<RoleDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.RoleDefinitions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : ToDefinition(row);
    }

    public Task<RoleDefinition> CreateAsync(string name, IReadOnlyList<string> permissions, CancellationToken ct = default)
    {
        var row = new RoleDefinitionRow
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsSystem = false,
            PermissionsJson = Serialize(permissions),
        };
        _db.RoleDefinitions.Add(row);
        return Task.FromResult(ToDefinition(row));
    }

    public async Task SetPermissionsAsync(Guid id, IReadOnlyList<string> permissions, CancellationToken ct = default)
    {
        var row = await _db.RoleDefinitions.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is not null) row.PermissionsJson = Serialize(permissions);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.RoleDefinitions.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is not null) _db.RoleDefinitions.Remove(row);
    }

    /// <summary>
    /// Lazy per-tenant seed: the first read against a tenant without expected roles inserts missing system
    /// roles from the hardcoded <see cref="RolePermissionDefaults"/> mapping. Done in code rather than as a
    /// per-tenant SQL seed in the migration so brand-new and upgraded tenants get deterministic role rows.
    /// Saves immediately — read paths have no unit-of-work commit of their own. A concurrent first read
    /// losing the unique-index race is fine: the rows exist by then either way.
    /// </summary>
    private async Task EnsureSystemRolesAsync(CancellationToken ct)
    {
        var existing = await _db.RoleDefinitions
            .Select(r => r.Name)
            .ToListAsync(ct);
        var existingNames = existing.ToHashSet(StringComparer.Ordinal);
        var requiredRoles = Enum.GetValues<UserRole>()
            .Select(role => role.ToString())
            .ToHashSet(StringComparer.Ordinal);
        if (requiredRoles.All(existingNames.Contains))
            return;

        foreach (var role in Enum.GetValues<UserRole>())
        {
            if (existingNames.Contains(role.ToString())) continue;

            _db.RoleDefinitions.Add(new RoleDefinitionRow
            {
                Id = Guid.NewGuid(),
                Name = role.ToString(),
                IsSystem = true,
                PermissionsJson = Serialize(RolePermissionDefaults.GetDefaults(role)),
            });
        }
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request seeded concurrently — detach our losing duplicates and carry on.
            foreach (var entry in _db.ChangeTracker.Entries<RoleDefinitionRow>()
                         .Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
        }
    }

    private static RoleDefinition ToDefinition(RoleDefinitionRow row)
        => new(row.Id, row.Name, row.IsSystem, Deserialize(row.PermissionsJson));

    private static string Serialize(IEnumerable<string> permissions)
        => JsonSerializer.Serialize(permissions.OrderBy(p => p, StringComparer.Ordinal).ToArray());

    private static IReadOnlyList<string> Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
