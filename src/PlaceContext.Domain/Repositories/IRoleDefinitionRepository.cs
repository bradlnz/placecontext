namespace PlaceContext.Domain.Repositories;

/// <summary>A named permission grant set members can be assigned — the editable, per-tenant form of
/// what <c>RolePermissionDefaults</c> hardcodes for the four system roles.</summary>
public sealed record RoleDefinition(Guid Id, string Name, bool IsSystem, IReadOnlyList<string> Permissions);

/// <summary>
/// Tenant-scoped role definitions. The four system roles (Viewer/Member/Admin/Owner) are materialized
/// lazily: any read against a tenant with no rows yet seeds them from the hardcoded
/// <c>RolePermissionDefaults</c> mapping, so fresh and pre-existing tenants behave identically without
/// a per-tenant SQL seed in the migration.
/// </summary>
public interface IRoleDefinitionRepository
{
    /// <summary>All role definitions for the tenant (seeding the system roles on first read).</summary>
    Task<IReadOnlyList<RoleDefinition>> ListAsync(CancellationToken ct = default);

    /// <summary>One role definition by exact name, or null — falling back to nothing; callers decide
    /// whether a missing row means the hardcoded defaults (system role) or an error (custom role).</summary>
    Task<RoleDefinition?> GetByNameAsync(string name, CancellationToken ct = default);

    /// <summary>One role definition by id, or null.</summary>
    Task<RoleDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Adds a custom (non-system) role. Uniqueness of the name is enforced by the
    /// (TenantId, Name) index; callers validate first for a friendly error.</summary>
    Task<RoleDefinition> CreateAsync(string name, IReadOnlyList<string> permissions, CancellationToken ct = default);

    /// <summary>Replaces a role's granted permission set.</summary>
    Task SetPermissionsAsync(Guid id, IReadOnlyList<string> permissions, CancellationToken ct = default);

    /// <summary>Deletes a role definition by id. No-op when the id does not exist.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
