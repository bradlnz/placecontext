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
    private readonly PlaceContextService _svc;
    private readonly OperationCenter _opCenter;
    private readonly BackgroundOperationRunner _ops;
    private readonly IWorkloadOutputBuffer _outputBuffer;
    private readonly IPermissionService _permissions;
    private readonly NavigationManager _navigation;
    private readonly ParameterPromptState _runPrompt = new();
    private readonly IJobChainSubmissionQueue? _submissions;
    private readonly IQemuEnvironmentCatalog? _qemuCatalog;
    private readonly IQemuLoginStore? _qemuLogins;
    private readonly ICurrentTenant? _tenant;
    private IDisposable? _operationSubscription;

    public JobChainsViewModel(
        PlaceContextService svc,
        OperationCenter opCenter,
        IWorkloadOutputBuffer outputBuffer,
        IPermissionService permissions,
        NavigationManager navigation,
        IJobChainSubmissionQueue? submissions = null,
        IQemuEnvironmentCatalog? qemuCatalog = null,
        ICurrentTenant? tenant = null,
        IQemuLoginStore? qemuLogins = null
    )
    {
        _svc = svc;
        _submissions = submissions;
        _qemuCatalog = qemuCatalog;
        _qemuLogins = qemuLogins;
        _tenant = tenant;
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
    private int _pageVersion;

    public async Task LoadPageAsync(Guid projectId, Guid? chainId, Guid? runId = null)
    {
        var version = ++_pageVersion;
        Initialize(projectId);
        await LoadAsync();
        if (version != _pageVersion) return;
        if (chainId is not { } id
            || Chains?.FirstOrDefault(chain => chain.Id == id) is not { } chain) return;
        if (!ShowEditor || EditChainId != id)
            await OpenChainEditorAsync(chain);
        // A bell notification deep-links to the actual paused run and its inputs,
        // not merely the workflow editor. Verify both project and chain ownership.
        if (runId is { } wanted && (OpenRun?.Id != wanted || !IsRunsTab))
        {
            var run = ChainRuns?.FirstOrDefault(x => x.Id == wanted)
                ?? await _svc.GetChainRunAsync(wanted);
            if (run is null || run.ProjectId != projectId || run.ChainId != id) return;
            EditorTab = "runs";
            await OpenChainRunAsync(run);
        }
    }

    public void Initialize(Guid projectId)
    {
        ProjectId = projectId;
        _operationSubscription?.Dispose();
        if (_tenant is { IsResolved: true })
            _operationSubscription = _opCenter.Subscribe(_tenant.TenantId, OnOpsChanged, projectId: projectId);
    }

    public void DetachEvents()
    {
        _pageVersion++;
        ShowEditor = false;
        _operationSubscription?.Dispose();
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
