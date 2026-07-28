namespace PlaceContext.Application.Ports;

/// <summary>One customizable slot in the portal navigation.</summary>
public sealed record MenuItemOverride(
    string Id,
    string? Label = null,
    int Order = 0,
    bool Visible = true,
    string? Section = null);

/// <summary>Tenant-owned menu layout for the workspace shell.</summary>
public sealed record MenuLayout(IReadOnlyList<MenuItemOverride> Workspace);

/// <summary>Resolved item ready to render (after layout + permissions).</summary>
public sealed record ResolvedMenuItem(
    string Id,
    string Label,
    string Kind,
    string? Href,
    string? Icon,
    string? Section,
    int Order,
    /// <summary>Id of the accordion group this item renders under; null = top level.</summary>
    string? Parent = null);

/// <summary>
/// Built-in menu catalog + per-tenant layout. Layout controls order, labels, visibility, and
/// section headers; the caller's effective permissions still hide items they cannot use.
/// </summary>
public interface IMenuConfigService
{
    /// <summary>Default layout (full product menu).</summary>
    MenuLayout DefaultLayout();

    /// <summary>Stored layout for the current tenant, or defaults when unset.</summary>
    Task<MenuLayout> GetLayoutAsync(CancellationToken ct = default);

    /// <summary>Persist layout (Settings → Menu). Requires settings.manage at the call site.</summary>
    Task SaveLayoutAsync(MenuLayout layout, CancellationToken ct = default);

    /// <summary>Workspace sidebar items for the current caller.</summary>
    Task<IReadOnlyList<ResolvedMenuItem>> GetWorkspaceMenuAsync(Guid? projectId, CancellationToken ct = default);

}
