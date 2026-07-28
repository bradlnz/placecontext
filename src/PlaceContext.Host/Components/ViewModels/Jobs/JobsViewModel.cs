using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobsViewModel : PageViewModel
{
    private readonly IPlaceContextService _svc;
    private readonly OperationCenter _opCenter;
    private readonly BackgroundOperationRunner _ops;
    private readonly ParameterPromptState _runPrompt = new();

    public JobsViewModel(IPlaceContextService svc, OperationCenter opCenter)
    {
        _svc = svc;
        _opCenter = opCenter;
        _ops = new BackgroundOperationRunner(opCenter);
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

    private CancellationTokenSource? _runPollCts;

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
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { Loading = false; NotifyStateChanged(); }
    }

}
