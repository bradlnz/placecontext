namespace PlaceContext.Application.Ports;

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
