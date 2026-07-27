using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ProjectDataViewModel : PageViewModel
{
    private readonly IPlaceContextService _svc;

    public ProjectDataViewModel(IPlaceContextService svc) => _svc = svc;

    // ── Parameters ────────────────────────────────────────────────────────────────────────────
    public Guid ProjectId { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    public void Initialize(Guid projectId)
    {
        if (ProjectId != projectId)
        {
            ProjectId = projectId;
            MonacoReady = false;
            MonacoLite = false;
            ViewMonacoReady = false;
        }
    }

    public async Task LoadAsync()
    {
        await RefreshTablesAsync();
        NotifyStateChanged();
    }

    public async Task RefreshTablesAsync()
    {
        try { Tables = await _svc.ListProjectDataTablesAsync(ProjectId); }
        catch (Exception ex) { Error = ex.Message; }
        finally { TablesReady = true; NotifyStateChanged(); }
    }

}
