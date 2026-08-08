namespace PlaceContext.Application.Ports;

/// <summary>One customizable slot in the portal navigation.</summary>
public sealed record MenuItemOverride(
    string Id,
    string? Label = null,
    int Order = 0,
    bool Visible = true,
    string? Section = null);
