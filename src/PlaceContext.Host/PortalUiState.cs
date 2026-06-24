namespace PlaceContext.Host;

/// <summary>
/// Scoped UI state shared between pages and the shell: the topbar title/subtitle the active page
/// sets on init. Kept tiny — the layout subscribes to <see cref="OnChanged"/> and re-renders.
/// </summary>
public sealed class PortalUiState
{
    public string Title { get; private set; } = "Overview";
    public string Sub { get; private set; } = "";

    public event Action? OnChanged;

    public void Set(string title, string sub)
    {
        Title = title;
        Sub = sub;
        OnChanged?.Invoke();
    }
}
