namespace PlaceContext.Host.Components.ViewModels;

public sealed class ClarificationResult
{
    public bool Confirmed { get; set; }
    public List<string> SelectedIds { get; set; } = new();
}
