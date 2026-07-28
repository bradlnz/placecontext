using System.Threading;
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
    private long _lastOpsChangeTick;

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
        // Debounce: ignore if less than 3s since last handled change
        var now = Environment.TickCount64;
        var last = Volatile.Read(ref _lastOpsChangeTick);
        if (now - last < 3000) return;
        Interlocked.Exchange(ref _lastOpsChangeTick, now);

        if (OpenRun is { } run)
            _ = RefreshRunsAsync(run.ChainId, openNewest: false);
    }

}
