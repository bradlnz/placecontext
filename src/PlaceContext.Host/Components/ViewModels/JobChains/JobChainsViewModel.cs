using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Build;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.Operations;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Workload.VirtualMachines;
using PlaceContext.Application;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel : PageViewModel, IComponentViewModel
{
    private readonly PlaceContextService _svc;
    private readonly OperationCenter _opCenter;
    private readonly IPermissionService _permissions;
    private readonly NavigationManager _navigation;
    private readonly ParameterPromptState _runPrompt = new();
    private readonly IJobChainSubmissionQueue? _submissions;
    private Guid? _pendingSubmissionId;
    private readonly IVirtualMachineEnvironmentCatalog? _qemuCatalog;
    private readonly IVirtualMachineLoginStore? _qemuLogins;
    private readonly ICurrentTenant? _tenant;
    private readonly IModelConnections? _modelConnections;
    private readonly IProjectAgentStore? _projectAgents;
    private readonly IBuildChatStore? _dashboardStore;
    private IDisposable? _operationSubscription;
    private readonly IModelCredentialsResolver? _reviewCredentials;
    public bool JevAvailable { get; private set; }
    public const string JevSetupMessage = "Connect Jev by adding TYPESAFE_API_KEY to this project’s vault or the server configuration.";

    public JobChainsViewModel(
        PlaceContextService svc,
        OperationCenter opCenter,
        IPermissionService permissions,
        NavigationManager navigation,
        IJobChainSubmissionQueue? submissions = null,
        IVirtualMachineEnvironmentCatalog? qemuCatalog = null,
        ICurrentTenant? tenant = null,
        IVirtualMachineLoginStore? qemuLogins = null,
        IOptions<RunBillingDeploymentOptions>? hosting = null, IModelConnections? modelConnections = null, IProjectAgentStore? projectAgents = null, IBuildChatStore? dashboardStore = null, IModelCredentialsResolver? reviewCredentials = null
    )
    {
        _svc = svc;
        _reviewCredentials = reviewCredentials;
        _modelConnections = modelConnections;
        _projectAgents = projectAgents;
        _dashboardStore = dashboardStore;
        _submissions = submissions;
        _qemuCatalog = qemuCatalog;
        _qemuLogins = qemuLogins;
        _tenant = tenant;
        _opCenter = opCenter;
        _permissions = permissions;
        _navigation = navigation;
    }

    // ── Parameters ────────────────────────────────────────────────────────────────────────────
    public Guid ProjectId
    {
        get; private set;
    }
    public string JobsRoute => PageRoutes.ProjectJobs(ProjectId);
    public string WorkflowsRoute => PageRoutes.ProjectWorkflows(ProjectId);
    public bool Embedded { get; set; }

    public void NavigateToJobs() => _navigation.NavigateTo(JobsRoute);

    private long _lastOpsChangeTick;

    public Guid? PendingChainRunId
    {
        get; private set;
    }
    public Guid? PendingChainId
    {
        get; private set;
    }

    public bool IsRunningChain(Guid chainId) => PendingChainId == chainId;

    public bool CanSendEmailAction
    {
        get; private set;
    }
    public bool CanSendSmsAction
    {
        get; private set;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    private int _pageVersion;

    public async Task LoadPageAsync(Guid projectId, Guid? chainId, Guid? runId = null, string? view = null)
    {
        var version = ++_pageVersion;
        var reuseLoadedPage = ProjectId == projectId && Chains is not null;
        Initialize(projectId);
        if (!reuseLoadedPage)
        {
            await LoadAsync();
        }
        if (version != _pageVersion)
        {
            return;
        }
        if (chainId is null)
        {
            if (ShowEditor && EditChainId is not null)
            {
                CloseEditor();
            }
            return;
        }
        if (Chains?.FirstOrDefault(chain => chain.Id == chainId) is not { } chain)
        {
            return;
        }
        var id = chain.Id;
        var openEditor = !ShowEditor || EditChainId != id;
        if (openEditor)
        {
            await OpenChainEditorAsync(chain, runId, view);
        }
        if (!openEditor)
        {
            EditorView = NormalizeEditorView(view ?? (runId.HasValue ? "runs" : "canvas"));
            if (IsMarkupView) PrepareMarkup();
            if (EditorView is "runs" or "details")
            {
                EditorTab = EditorView;
            }
            if (runId is null && OpenRun is not null)
            {
                StopPolling();
                OpenRun = null;
                StepRunDetail = null;
                _executionStepIndex = null;
            }
        }
        // Deep links must stay within the current project and workflow.
        if (runId is { } wanted && (OpenRun?.Id != wanted || !IsRunsTab))
        {
            var run = ChainRuns?.FirstOrDefault(x => x.Id == wanted)
                ?? await _svc.GetChainRunAsync(wanted);
            if (run is null || run.ProjectId != projectId || run.ChainId != id)
            {
                return;
            }
            EditorTab = "runs";
            EditorView = "runs";
            await OpenChainRunAsync(run);
        }
    }

    public void Initialize(Guid projectId)
    {
        ProjectId = projectId;
        _operationSubscription?.Dispose();
        if (_tenant is { IsResolved: true })
        {
            _operationSubscription = _opCenter.Subscribe(_tenant.TenantId, OnOpsChanged, projectId: projectId);
        }
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
            await RefreshJevAvailabilityAsync();
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

    private async Task RefreshJevAvailabilityAsync()
    {
        JevAvailable = false;
        if (_reviewCredentials is null) return;
        try
        {
            var credentials = await _reviewCredentials.ResolveAsync(ProjectId);
            JevAvailable = !string.IsNullOrWhiteSpace(credentials.ApiKey)
                && !credentials.ApiKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));
        }
        catch (Exception) { /* The picker stays disabled when configuration cannot be read. */ }
    }

    private void OnOpsChanged()
    {
        // Debounce: ignore if less than 3s since last handled change
        var now = Environment.TickCount64;
        var last = Volatile.Read(ref _lastOpsChangeTick);
        if (now - last < 3000)
        {
            return;
        }
        Interlocked.Exchange(ref _lastOpsChangeTick, now);

        if (OpenRun is { } run)
        {
            _ = RefreshRunsAsync(run.ChainId);
        }
    }
}
