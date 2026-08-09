namespace PlaceContext.Settings.Contracts.Configuration;

public sealed record MenuItemOverride(
    string Id,
    string? Label = null,
    int Order = 0,
    bool Visible = true,
    string? Section = null);
