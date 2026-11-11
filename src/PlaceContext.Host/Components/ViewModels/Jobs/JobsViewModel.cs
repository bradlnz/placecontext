using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.Observability;
using PlaceContext.Application.Operations;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application;
using PlaceContext.Domain.Mcp;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobsViewModel : PageViewModel
{
    private static event EventHandler? McpOAuthCompleted;
    private readonly PlaceContextService _svc;
    private readonly OperationCenter _opCenter;
    private readonly IJobRunSubmissionQueue _submissions;
    private readonly ParameterPromptState _runPrompt = new();
    private readonly IJSRuntime _js;
    private readonly NavigationManager _navigation;
    private readonly ICurrentTenant? _tenant;
    private IDisposable? _operationSubscription;

    public JobsViewModel(
        PlaceContextService svc,
        OperationCenter opCenter,
        IJSRuntime js,
        NavigationManager navigation,
        ICurrentTenant? tenant = null,
        IJobRunSubmissionQueue? submissions = null
    )
    {
        _svc = svc;
        _opCenter = opCenter;
        _submissions = submissions ?? throw new InvalidOperationException("Durable job submissions are not configured.");
        _js = js;
        _navigation = navigation;
        _tenant = tenant;
        McpOAuthCompleted += OnMcpOAuthCompleted;
    }

    // ── Public state (bound by markup) ─────────────────────────────────────────────────────────
    public Guid ProjectId
    {
        get; private set;
    }

    public int EnabledTriggerCount => Triggers?.Count(trigger => trigger.Enabled) ?? 0;
    public int AutomatedJobCount => Jobs?.Count(job => EnabledTriggersFor(job.Id) > 0) ?? 0;
    public int AutomationPercent =>
        Jobs is not { Count: > 0 } ? 0 : (int)Math.Round(AutomatedJobCount * 100d / Jobs.Count);

    public int EnabledTriggersFor(Guid jobId) =>
        Triggers?.Count(trigger => trigger.JobId == jobId && trigger.Enabled) ?? 0;

    public static string WorkloadLabel(JobView job) =>
        job.MapSourceKind == JobSourceCatalog.Code
            ? $"{(string.IsNullOrWhiteSpace(job.MapRuntimeId) ? JobSourceCatalog.Code : job.MapRuntimeId)} code"
            : JobSourceCatalog.Container;

    public bool IsCodeJob(JobView job) => job.MapSourceKind == JobSourceCatalog.Code;

    public MapSourceMode MapMode =>
        EdMapSourceKind.Equals("code", StringComparison.OrdinalIgnoreCase)
            ? MapSourceMode.Code
            : MapSourceMode.Image;
    public bool IsImageMap => MapMode == MapSourceMode.Image;
    public bool IsCodeMap => MapMode == MapSourceMode.Code;

    public void SelectImageMap()
    {
        EdMapSourceKind = "image";
        ResetSourceEditor();
    }

    public void SelectCodeMap() => EdMapSourceKind = "code";

    public bool IsPayloadForm => EdPayloadMode.Equals("form", StringComparison.OrdinalIgnoreCase);
    public bool IsPayloadRaw => !IsPayloadForm;

    public void SelectPayloadForm() => SwitchPayloadMode("form");

    public void SelectPayloadRaw() => SwitchPayloadMode("raw");

    public bool IsDetailsTab => EditorTab.Equals("details", StringComparison.OrdinalIgnoreCase);
    public bool IsRunsTab => EditorTab.Equals("runs", StringComparison.OrdinalIgnoreCase);
    public bool IsTriggersTab => EditorTab.Equals("triggers", StringComparison.OrdinalIgnoreCase);

    public bool IsMcpEnabled(Guid id) => EdMcpConnectionIds.Contains(id.ToString());

    public bool IsMcpOAuth(McpConnectionView connection) =>
        string.Equals(connection.AuthType, McpAuthType.OAuth, StringComparison.OrdinalIgnoreCase);

    public bool IsMcpConnected(McpConnectionView connection) =>
        connection.LastStatus?.StartsWith("oauth:connected", StringComparison.OrdinalIgnoreCase)
        == true;

    public string McpStatusClass(McpConnectionView connection) =>
        Presentation.IsExpired(connection.OAuthTokenExpiresAt) ? "expired"
        : IsMcpConnected(connection) ? "active"
        : string.Empty;

    public string McpStatusLabel(McpConnectionView connection) =>
        Presentation.IsExpired(connection.OAuthTokenExpiresAt) ? "token expired"
        : IsMcpConnected(connection) ? "oauth connected"
        : "oauth";

    public string McpAuthLabel(McpConnectionView connection) =>
        Presentation.IsExpired(connection.OAuthTokenExpiresAt) ? "Reconnect" : "Auth";

    public void NavigateToTests() => _navigation.NavigateTo(PageRoutes.ProjectTests(ProjectId));

    public void NavigateToJob(Guid jobId) =>
        _navigation.NavigateTo(PageRoutes.ProjectJob(ProjectId, jobId));

    public void NavigateToSecrets() => _navigation.NavigateTo(PageRoutes.ProjectSecrets(ProjectId));

    public Task OpenMcpOAuthAsync(Guid connectionId) =>
        _js.InvokeVoidAsync("open", GetOAuthUrl(connectionId), "_blank").AsTask();

    public Task InitializeOAuthListenerAsync() =>
        _js.InvokeVoidAsync(
                "eval",
                "if (!window.__jobsMcpOAuthListener) { window.__jobsMcpOAuthListener = function(e) { if (e.data && e.data.startsWith && e.data.startsWith('mcp-oauth-')) DotNet.invokeMethodAsync('PlaceContext.Host', 'OnJobsMcpOAuthCallback', e.data); }; window.addEventListener('message', window.__jobsMcpOAuthListener); }"
            )
            .AsTask();

    public IReadOnlyList<JobView>? Jobs
    {
        get; private set;
    }
    public IReadOnlyList<ProjectSecretView>? VaultSecrets
    {
        get; private set;
    }
    public IReadOnlyList<JobRunView>? Runs
    {
        get; private set;
    }
    public JobRunDetailView? RunDetail
    {
        get; private set;
    }
    public IReadOnlyList<RunArtifactLinkView>? RunArtifacts
    {
        get; private set;
    }
    public IReadOnlyList<JobRunTelemetry>? JobTelemetry
    {
        get; private set;
    }
    public Guid? SelectedJobId
    {
        get; private set;
    }
    public Guid? RunningJobId
    {
        get; private set;
    }
    public Guid? PendingRunId
    {
        get; private set;
    }
    public Guid? PendingRunJobId
    {
        get; private set;
    }
    public string? Message
    {
        get; private set;
    }
    public bool Loading { get; private set; } = true;
    public Guid? ConfirmDeleteId
    {
        get; set;
    }

    // ── MCP connections ────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<McpConnectionView>? ProjectMcpConnections
    {
        get; private set;
    }
    public HashSet<string> EdMcpConnectionIds { get; } = new();

    private int _paneVersion;
    private CancellationTokenSource? _runPollCts;
    private CancellationTokenSource? _runDetailCts;

    // ── Run-detail auto-refresh (polls while the detail is open and the run is active) ──────────
    private void StartRunDetailPolling(Guid runId)
    {
        StopRunDetailPolling();
        _runDetailCts = new CancellationTokenSource();
        var ct = _runDetailCts.Token;
        var version = _paneVersion;
        _ = Task.Run(
            async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(2000, ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    try
                    {
                        var detail = await _svc.GetJobRunAsync(runId);
                        var artifacts = await _svc.ListRunArtifactsAsync(runId);
                        if (ct.IsCancellationRequested || version != _paneVersion || RunDetail?.Id != runId)
                        {
                            break;
                        }
                        RunDetail = detail;
                        RunArtifacts = artifacts;
                        NotifyStateChanged();
                        _ = ScrollLogsDown();

                        if (detail?.Status is "Succeeded" or "Failed" or "Partial")
                        {
                            break;
                        }
                    }
                    catch { }
                }
            },
            ct
        );
    }

    private void StopRunDetailPolling()
    {
        _runDetailCts?.Cancel();
        _runDetailCts?.Dispose();
        _runDetailCts = null;
    }

    private async Task ScrollLogsDown()
    {
        try
        {
            await _js.InvokeVoidAsync("placecontext.scrollLogs");
        }
        catch { }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    public void Initialize(Guid projectId)
    {
        if (ProjectId != projectId)
        {
            ResetPaneState();
            _editorMetadataLoaded = false;
            VaultSecrets = null;
            EventTypes = null;
            ProjectMcpConnections = null;
        }
        ProjectId = projectId;
        _operationSubscription?.Dispose();
        if (_tenant is { IsResolved: true })
        {
            _operationSubscription = _opCenter.Subscribe(_tenant.TenantId, OnOpsChanged, projectId: projectId);
        }
        McpOAuthCompleted -= OnMcpOAuthCompleted;
        McpOAuthCompleted += OnMcpOAuthCompleted;
    }

    public async Task LoadPageAsync(Guid projectId, Guid? jobId, string? tab)
    {
        Initialize(projectId);
        var version = _paneVersion;
        await LoadAsync();
        if (version != _paneVersion)
        {
            return;
        }
        if (jobId is { } id && Jobs?.FirstOrDefault(job => job.Id == id) is { } job)
        {
            if (!ShowEditor || EditJobId != id)
            {
                await OpenJobAsync(job);
            }
            if (ShowEditor && EditJobId == id && tab == "triggers")
            {
                EditorTab = "triggers";
            }
        }
    }

    private void InvalidateRunRequests()
    {
        _paneVersion++;
        StopRunPolling();
        StopRunDetailPolling();
    }

    private void ResetPaneState()
    {
        _appliedTemplate = null;
        _templateDrafts.Clear();
        InvalidateRunRequests();
        ShowEditor = false;
        ShowTemplateModal = false;
        RunPromptJob = null;
        RunDetail = null;
        RunArtifacts = null;
        SelectedJobId = null;
        PendingRunId = null;
        PendingRunJobId = null;
        EditJobId = null;
        SelectedTriggerId = null;
        EditorError = null;
        EditorTab = "details";
        ResetSourceEditor();
    }

    [JSInvokable]
    public static Task OnJobsMcpOAuthCallback(string message)
    {
        McpOAuthCompleted?.Invoke(null, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void DetachEvents()
    {
        _operationSubscription?.Dispose();
        McpOAuthCompleted -= OnMcpOAuthCompleted;
        ResetPaneState();
    }

    private async void OnMcpOAuthCompleted(object? sender, EventArgs args)
    {
        await LoadMcpConnectionsAsync();
    }

    public async Task LoadAsync()
    {
        _editorMetadataLoaded = false;
        Loading = true;
        Message = null;
        try
        {
            Jobs = await _svc.ListJobsAsync(ProjectId);
            Loading = false;
            NotifyStateChanged();
            Triggers = await _svc.ListTriggersAsync(ProjectId);
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

    public async Task LoadMcpConnectionsAsync()
    {
        try
        {
            ProjectMcpConnections = await _svc.ListMcpConnectionsAsync(ProjectId);
        }
        catch { }
        NotifyStateChanged();
    }

    public void ToggleMcpConnection(Guid connectionId, bool enabled)
    {
        var id = connectionId.ToString();
        if (enabled)
        {
            EdMcpConnectionIds.Add(id);
            return;
        }
        EdMcpConnectionIds.Remove(id);
    }

    public string GetOAuthUrl(Guid connectionId) => $"/mcp-oauth/start?connectionId={connectionId}";
}
