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

    /// <summary>True when the current page is rendered inside ProjectLayout or SettingsLayout.</summary>
    public bool HasSubNav { get; private set; }

    /// <summary>Registered by a sub-layout to open/close its own slide-over side menu.</summary>
    public Action? ToggleSubNav { get; private set; }

    /// <summary>Registered by MainLayout so a sub-layout can open the main workspace sidebar.</summary>
    public Action? OpenMainNav { get; private set; }

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

    public void SetSubNav(bool hasSubNav, Action? toggleSubNav = null)
    {
        HasSubNav = hasSubNav;
        ToggleSubNav = toggleSubNav;
        OnChanged?.Invoke();
    }

    public void SetMainNavOpener(Action openMainNav)
    {
        OpenMainNav = openMainNav;
        OnChanged?.Invoke();
    }
}
