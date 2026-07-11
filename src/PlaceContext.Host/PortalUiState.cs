namespace PlaceContext.Host;

/// <summary>
/// Scoped UI state shared between pages and the shell: the topbar title/subtitle the active page
/// sets on init. Kept tiny — the layout subscribes to <see cref="OnChanged"/> and re-renders.
/// </summary>
public sealed class PortalUiState
{
    public string Title { get; private set; } = "Dashboard";
    public string Sub { get; private set; } = "";

    /// <summary>The project the sidebar's switcher is set to — project-scoped nav links target it.</summary>
    public Guid? CurrentProjectId { get; private set; }
    public string? CurrentProjectName { get; private set; }

    public event Action? OnChanged;

    public void Set(string title, string sub)
    {
        Title = title;
        Sub = sub;
        OnChanged?.Invoke();
    }

    public void SetProject(Guid id, string name)
    {
        if (CurrentProjectId == id && CurrentProjectName == name) return;
        CurrentProjectId = id;
        CurrentProjectName = name;
        OnChanged?.Invoke();
    }
}
