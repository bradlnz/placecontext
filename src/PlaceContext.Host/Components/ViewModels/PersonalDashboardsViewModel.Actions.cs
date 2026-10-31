using PlaceContext.Application.Dashboards;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class PersonalDashboardsViewModel
{
    public IReadOnlyList<WorkflowApprovalAction> WorkflowActions { get; private set; } = [];
    public string? ActionError { get; private set; }
    public string? ActionNotice { get; private set; }
    public string? SelectedWorkflowActionId { get; private set; }
    public bool? ProposedApproval { get; private set; }
    public WorkflowApprovalAction? SelectedWorkflowAction => WorkflowActions.FirstOrDefault(x => x.ActionId == SelectedWorkflowActionId);
    public int PendingHumanActionCount => WorkflowActions.Count;
    public bool ActionSubmitting { get; private set; }

    // This queue is independently sourced from real, durable workflow checkpoints.
    // No model inference or locally simulated indicator can manufacture an approvable action.
    private async Task LoadWorkflowActionsCoreAsync()
    {
        SelectedWorkflowActionId = null;
        ProposedApproval = null;
        ActionError = null;
        if (actionInbox is null)
        {
            WorkflowActions = [];
            return;
        }
        try { WorkflowActions = await actionInbox.ListAsync(ProjectId, _stop.Token); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch
        {
            WorkflowActions = [];
            ActionError = "Workflow actions could not be loaded. Try Refresh to check for pending approvals.";
        }
    }

    public Task RefreshWorkflowActionsAsync() => ExecuteAsync(async () =>
    {
        await LoadWorkflowActionsCoreAsync();
        if (ActionError is null) ActionNotice = "Workflow requests refreshed.";
    });

    public void ShowActions()
    {
        if (Busy) return;
        Lens = DashboardLens.Actions;
        _rendered.Clear();
        ActionError = null;
    }

    public void ProposeHumanResponse(string actionId, bool approved)
    {
        if (Busy || ActionSubmitting || WorkflowActions.All(x => x.ActionId != actionId)) return;
        SelectedWorkflowActionId = actionId;
        ProposedApproval = approved;
        ActionError = ActionNotice = null;
    }

    public void CancelHumanResponse()
    {
        if (Busy || ActionSubmitting) return;
        SelectedWorkflowActionId = null;
        ProposedApproval = null;
    }

    public Task SubmitHumanResponseAsync() =>
        SelectedWorkflowAction is not { } selected || ProposedApproval is not { } answer || actionInbox is null
            ? Task.CompletedTask : ExecuteAsync(async () =>
            {
                ActionSubmitting = true;
                ActionError = ActionNotice = null;
                try
                {
                    // The server re-reads the checkpoint and checks permissions and request ID
                    // before writing the response; no client-supplied state controls execution.
                    await actionInbox.RespondAsync(ProjectId, selected.ChainRunId, selected.RequestId,
                        answer, _stop.Token);
                    SelectedWorkflowActionId = null;
                    ProposedApproval = null;
                    WorkflowActions = await actionInbox.ListAsync(ProjectId, _stop.Token);
                    ActionNotice = answer
                        ? "Approval response recorded. The paused workflow was queued to continue according to its configured branches."
                        : "Decline response recorded. The paused workflow was queued to follow its configured No branch; verify that branch before using this action.";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ActionError = "Could not submit this response. The approval may have changed or you may lack permission. Refresh the inbox before retrying.";
                }
                finally { ActionSubmitting = false; }
            });
}
