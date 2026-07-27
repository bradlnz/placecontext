using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel : PageViewModel
{
    private readonly IPlaceContextService _svc;
    private readonly OperationCenter _opCenter;
    private readonly BackgroundOperationRunner _ops;
    private readonly ParameterPromptState _runPrompt = new();

    public JobChainsViewModel(IPlaceContextService svc, OperationCenter opCenter)
    {
        _svc = svc;
        _opCenter = opCenter;
        _ops = new BackgroundOperationRunner(opCenter);
    }

    // ── Parameters ────────────────────────────────────────────────────────────────────────────
    public Guid ProjectId { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    public void Initialize(Guid projectId)
    {
        ProjectId = projectId;
        _opCenter.Changed += OnOpsChanged;
    }

    public void DetachEvents()
    {
        _opCenter.Changed -= OnOpsChanged;
        StopPolling();
    }

    public async Task LoadAsync()
    {
        Loading = true;
        Message = null;
        try
        {
            Chains = await _svc.ListJobChainsAsync(ProjectId);
            Jobs = await _svc.ListJobsAsync(ProjectId);
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { Loading = false; NotifyStateChanged(); }
    }

    private void OnOpsChanged()
    {
        if (OpenRun is { } run)
            _ = RefreshRunsAsync(run.ChainId, openNewest: false);
    }

}
