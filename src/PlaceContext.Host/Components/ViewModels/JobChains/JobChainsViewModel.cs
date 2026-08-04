using System.Threading;
using Microsoft.AspNetCore.Components;
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
    private readonly IWorkloadOutputBuffer _outputBuffer;
    private readonly IPermissionService _permissions;
    private readonly NavigationManager _navigation;
    private readonly ParameterPromptState _runPrompt = new();

    public JobChainsViewModel(
        IPlaceContextService svc,
        OperationCenter opCenter,
        IWorkloadOutputBuffer outputBuffer,
        IPermissionService permissions,
        NavigationManager navigation
    )
    {
        _svc = svc;
        _opCenter = opCenter;
        _outputBuffer = outputBuffer;
        _permissions = permissions;
        _navigation = navigation;
        _ops = new BackgroundOperationRunner(opCenter);
    }

    // ── Parameters ────────────────────────────────────────────────────────────────────────────
    public Guid ProjectId { get; private set; }
    public string JobsRoute => PageRoutes.ProjectJobs(ProjectId);

    public void NavigateToJobs() => _navigation.NavigateTo(JobsRoute);

    private long _lastOpsChangeTick;

    public Guid? PendingChainRunId { get; private set; }
    public Guid? PendingChainId { get; private set; }

    public bool IsRunningChain(Guid chainId) => PendingChainId == chainId;

    public bool CanSendEmailAction { get; private set; }
    public bool CanSendSmsAction { get; private set; }

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
            CanSendEmailAction = await _permissions.HasAsync(Permission.EmailSend);
            CanSendSmsAction = await _permissions.HasAsync(Permission.SmsSend);
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Loading = false;
            NotifyStateChanged();
        }
    }

    private void OnOpsChanged()
    {
        // Debounce: ignore if less than 3s since last handled change
        var now = Environment.TickCount64;
        var last = Volatile.Read(ref _lastOpsChangeTick);
        if (now - last < 3000)
            return;
        Interlocked.Exchange(ref _lastOpsChangeTick, now);

        if (OpenRun is { } run)
            _ = RefreshRunsAsync(run.ChainId);
    }
}
