namespace PlaceContext.Domain.Repositories;

/// <summary>A named permission grant set members can be assigned — the editable, per-tenant form of
/// what <c>RolePermissionDefaults</c> hardcodes for the four system roles.</summary>
public sealed record RoleDefinition(Guid Id, string Name, bool IsSystem, IReadOnlyList<string> Permissions);
