using PlaceContext.Application.Workflows;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class WorkflowMonitorViewModel(IWorkflowMonitor monitor, PortalUiState ui)
    : PageViewModel, IComponentViewModel, IDisposable
{
    public Guid ProjectId { get; private set; }
    public WorkflowMonitorSnapshot? Snapshot { get; private set; }
    public bool Busy { get; private set; }
    public bool Submitting { get; private set; }
    public string? Error { get; private set; }
    public string? Notice { get; private set; }
    public WorkflowHumanAction? PendingConfirmation { get; private set; }
    public bool? ProposedAnswer { get; private set; }

    private readonly CancellationTokenSource _stop = new();
    private Task _operation = Task.CompletedTask;
    private bool _disposed;

    public async Task LoadAsync(Guid projectId)
    {
        if (_disposed || projectId == Guid.Empty) return;
        await _operation;
        if (ProjectId == projectId && Snapshot is not null) return;
        ProjectId = projectId;
        Snapshot = null; PendingConfirmation = null; ProposedAnswer = null;
        ui.Set("Monitor", "Workflow results and human actions");
        await RefreshAsync();
    }

    public Task RefreshAsync() => ExecuteAsync(async () =>
    {
        PendingConfirmation = null;
        ProposedAnswer = null;
        Snapshot = await monitor.LoadAsync(ProjectId, _stop.Token);
    });

    public void Propose(WorkflowHumanAction action, bool answer)
    {
        if (Busy || Snapshot?.CanRespond != true
            || !action.CanAnswerYesNo
            || !Snapshot.Actions.Any(x => x.Key == action.Key))
            return;
        PendingConfirmation = action; ProposedAnswer = answer;
        Error = Notice = null;
        NotifyStateChanged();
    }

    public void Cancel()
    {
        if (Busy) return;
        PendingConfirmation = null; ProposedAnswer = null;
        NotifyStateChanged();
    }

    public Task ConfirmAsync()
    {
        if (PendingConfirmation is not { } action || ProposedAnswer is not { } answer
            || Snapshot?.CanRespond != true || !action.CanAnswerYesNo
            || !Snapshot.Actions.Any(x => x.Key == action.Key))
            return Task.CompletedTask;

        return ExecuteAsync(async () =>
        {
            Submitting = true;
            try
            {
                await monitor.RespondAsync(ProjectId, action.RunId, action.RequestId, answer, _stop.Token);
                PendingConfirmation = null;
                ProposedAnswer = null;
                // Don't claim completion until the backend confirms acceptance.
                Notice = answer
                    ? "Yes response recorded. The paused workflow is scheduled to continue on its configured path."
                    : "No response recorded. The paused workflow is scheduled to follow its configured No path; this does not necessarily stop it.";
                Snapshot = await monitor.LoadAsync(ProjectId, _stop.Token);
            }
            finally { Submitting = false; }
        });
    }

    private Task ExecuteAsync(Func<Task> operation)
    {
        if (_disposed || Busy) return Task.CompletedTask;
        return _operation = ExecuteCoreAsync(operation);
    }

    private async Task ExecuteCoreAsync(Func<Task> operation)
    {
        Busy = true; Error = null; NotifyStateChanged();
        try { await operation(); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { Error = ex is UnauthorizedAccessException
            ? "You do not have permission for this action."
            : "Could not complete this operation. Refresh Monitor to check the current workflow state."; }
        finally { Busy = false; NotifyStateChanged(); }
    }

    public static string StatusIcon(string status) => status switch
    {
        "Succeeded" => "check-circle",
        "Failed" or "Partial" => "warning",
        "AwaitingInput" => "human",
        "Running" or "Waiting" => "clock",
        "Cancelled" => "close",
        _ => "chain"
    };

    public static string StatusTone(string status) => status switch
    {
        "Succeeded" => "good",
        "Failed" or "Partial" => "bad",
        "AwaitingInput" => "attention",
        "Running" or "Waiting" => "active",
        _ => "neutral"
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _stop.Dispose();
        Detach();
    }
}
