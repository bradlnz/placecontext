namespace PlaceContext.Infrastructure.Persistence;

/// <summary>
/// One role definition per tenant: a named permission grant set members can be assigned. The system
/// roles (Viewer/Member/Admin/Owner) are materialized lazily on first read from the hardcoded
/// <c>RolePermissionDefaults</c> mapping; custom roles are created from the Access settings UI.
/// <see cref="PermissionsJson"/> is a JSON array of granted permission strings (see the
/// <c>Permission</c> catalog).
/// </summary>
public sealed class RoleDefinitionRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>System roles ship with the product and cannot be deleted (Owner is also non-editable).</summary>
    public bool IsSystem { get; set; }
    public string PermissionsJson { get; set; } = "[]";
}
