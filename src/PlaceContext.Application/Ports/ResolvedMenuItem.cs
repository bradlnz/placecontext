namespace PlaceContext.Application.Ports;

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
