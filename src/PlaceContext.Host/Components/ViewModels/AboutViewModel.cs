namespace PlaceContext.Host.Components.ViewModels;

public sealed class AboutViewModel : PageViewModel
{
    private readonly PortalUiState _ui;
    private readonly TimeProvider _clock;

    public AboutViewModel(PortalUiState ui, TimeProvider? clock = null)
    {
        _ui = ui;
        _clock = clock ?? TimeProvider.System;
    }

    public int CopyrightYear
    {
        get; private set;
    }

    public void Initialize()
    {
        CopyrightYear = _clock.GetUtcNow().Year;
        _ui.Set("About", "PlaceContext — data workflow engine");
    }
}
