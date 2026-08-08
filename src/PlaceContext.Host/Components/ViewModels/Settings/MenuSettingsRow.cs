namespace PlaceContext.Host.Components.ViewModels;

public sealed class MenuSettingsRow
{
    public string Id { get; init; } = string.Empty;
    public string DefaultLabel { get; init; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool Visible { get; set; } = true;
    public string Section { get; set; } = string.Empty;
}
