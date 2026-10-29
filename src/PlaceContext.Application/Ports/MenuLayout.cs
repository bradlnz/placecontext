namespace PlaceContext.Application.Ports;

/// <summary>Tenant-owned menu layout for the workspace shell.</summary>
public sealed record MenuLayout(IReadOnlyList<MenuItemOverride> Workspace);
