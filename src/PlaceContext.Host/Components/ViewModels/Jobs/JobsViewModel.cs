using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;
using PlaceContext.Infrastructure.Operations;
using Microsoft.JSInterop;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobsViewModel : PageViewModel
{
    private readonly IPlaceContextService _svc;
    private readonly OperationCenter _opCenter;
    private readonly BackgroundOperationRunner _ops;
    private readonly ParameterPromptState _runPrompt = new();
    private readonly IJSRuntime _js;

    public JobsViewModel(IPlaceContextService svc, OperationCenter opCenter, IJSRuntime js)
    {
        _svc = svc;
        _opCenter = opCenter;
        _ops = new BackgroundOperationRunner(opCenter);
        _js = js;
    }

    // ── Public state (bound by markup) ─────────────────────────────────────────────────────────
    public Guid ProjectId { get; private set; }

    public IReadOnlyList<JobView>? Jobs { get; private set; }
    public IReadOnlyList<ProjectSecretView>? VaultSecrets { get; private set; }
    public IReadOnlyList<JobRunView>? Runs { get; private set; }
    public JobRunDetailView? RunDetail { get; private set; }
    public IReadOnlyList<RunArtifactLinkView>? RunArtifacts { get; private set; }
    public IReadOnlyList<JobRunTelemetry>? JobTelemetry { get; private set; }
    public Guid? SelectedJobId { get; private set; }
    public Guid? RunningJobId { get; private set; }
    public Guid? PendingRunId { get; private set; }
    public Guid? PendingRunJobId { get; private set; }
    public string? Message { get; private set; }
    public bool Loading { get; private set; } = true;
    public Guid? ConfirmDeleteId { get; set; }

    // ── MCP connections ────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<McpConnectionView>? ProjectMcpConnections { get; private set; }
    public HashSet<string> EdMcpConnectionIds { get; } = new();

    private CancellationTokenSource? _runPollCts;
    private CancellationTokenSource? _runDetailCts;

    // ── Run-detail auto-refresh (polls while the detail is open and the run is active) ──────────
    private void StartRunDetailPolling(Guid runId)
    {
        StopRunDetailPolling();
        _runDetailCts = new CancellationTokenSource();
        var ct = _runDetailCts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) break;

                try
                {
                    var detail = await _svc.GetJobRunAsync(runId);
                    if (ct.IsCancellationRequested) break;

                    RunDetail = detail;
                    RunArtifacts = await _svc.ListRunArtifactsAsync(runId);
                    NotifyStateChanged();
                    _ = ScrollLogsDown();

                    if (detail?.Status is "Succeeded" or "Failed" or "Partial")
                        break;
                }
                catch { }
            }
        }, ct);
    }

    private void StopRunDetailPolling()
    {
        _runDetailCts?.Cancel();
        _runDetailCts?.Dispose();
        _runDetailCts = null;
    }

    private async Task ScrollLogsDown()
    {
        try { await _js.InvokeVoidAsync("placecontext.scrollLogs"); } catch { }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    public void Initialize(Guid projectId)
    {
        ProjectId = projectId;
        _opCenter.Changed += OnOpsChanged;
    }

    public void DetachEvents()
    {
        _opCenter.Changed -= OnOpsChanged;
        StopRunPolling();
        StopRunDetailPolling();
    }

    public async Task LoadAsync()
    {
        Loading = true;
        Message = null;
        try
        {
            Jobs = await _svc.ListJobsAsync(ProjectId);
            VaultSecrets = await _svc.ListProjectSecretsAsync(ProjectId);
            Triggers = await _svc.ListTriggersAsync(ProjectId);
            EventTypes = await _svc.ListEventTypesAsync();
            ProjectMcpConnections = await _svc.ListMcpConnectionsAsync(ProjectId);
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { Loading = false; NotifyStateChanged(); }
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
        if (enabled) EdMcpConnectionIds.Add(id);
        else EdMcpConnectionIds.Remove(id);
    }

    public string GetOAuthUrl(Guid connectionId) => $"/mcp-oauth/start?connectionId={connectionId}";

}
