using PlaceContext.Application;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class DataGraphViewModel : PageViewModel
{
    private readonly IPlaceContextService _service;

    public DataGraphViewModel(IPlaceContextService service) => _service = service;

    public Guid ProjectId { get; private set; }
    public GraphVizView? Graph { get; private set; }
    public bool Loading { get; private set; }
    public string? Error { get; private set; }

    public async Task LoadAsync(Guid projectId)
    {
        ProjectId = projectId;
        Loading = true;
        Error = null;
        Graph = null;
        NotifyStateChanged();

        try
        {
            Graph = await _service.GetGraphVizAsync(projectId);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Loading = false;
            NotifyStateChanged();
        }
    }
}
