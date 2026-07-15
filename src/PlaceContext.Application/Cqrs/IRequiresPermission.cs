namespace PlaceContext.Application.Cqrs;

/// <summary>
/// Optional marker for a command that guards a sensitive, state-changing action (e.g. deleting an
/// artifact, importing a backup manifest, managing vault secrets). When a command implements this, the
/// <see cref="Dispatcher"/> checks the caller's effective permissions before the handler runs — one
/// server-side choke point that applies no matter how the command was reached (a Blazor button, a
/// facade call from a minimal API endpoint, or a future MCP tool), so hiding the triggering UI element
/// is never the only thing standing between a caller and the action.
/// </summary>
public interface IRequiresPermission
{
    /// <summary>The permission string (see <see cref="PlaceContext.Application.Ports.Permission"/>) required to run this command.</summary>
    string RequiredPermission { get; }
}
