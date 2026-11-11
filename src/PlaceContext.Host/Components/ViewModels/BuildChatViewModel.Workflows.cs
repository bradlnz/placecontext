using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class BuildChatViewModel
{
    public bool WorkflowPickerOpen { get; private set; }
    public bool WorkflowPickerLoading { get; private set; }
    public string? WorkflowPickerError { get; private set; }
    public IReadOnlyList<JobChainView> AvailableWorkflows { get; private set; } = [];
    private int _workflowPickerVersion;
    private int _handledWorkflowPicker;

    public async Task StartWorkflowChatAsync()
    {
        if (NavigationLocked)
        {
            return;
        }
        await OpenWorkflowPickerAsync();
    }

    private async Task OpenWorkflowPickerAsync()
    {
        AgentOpen = false;
        ClosePreview();
        NavigateToDashboard();
        OpenWorkspace();
        WorkflowPickerOpen = WorkflowPickerLoading = true;
        WorkflowPickerError = null;
        AvailableWorkflows = [];
        var projectId = Conversation.ProjectId;
        var version = ++_workflowPickerVersion;
        NotifyStateChanged();
        try
        {
            if (!await permissions.HasAsync(Permission.ProjectsView)
                || !await permissions.HasAsync(Permission.ChainsManage)
                || !await permissions.HasAsync(Permission.JobsRun))
            {
                throw new UnauthorizedAccessException();
            }
            var workflows = await service.ListJobChainsAsync(projectId);
            if (version == _workflowPickerVersion)
            {
                AvailableWorkflows = workflows.Where(w => w.ProjectId == projectId).OrderBy(w => w.Name).ToArray();
            }
        }
        catch (Exception)
        {
            if (version == _workflowPickerVersion)
            {
                WorkflowPickerError = "Could not load workflows. Check project access and run permissions, then try again.";
            }
        }
        finally
        {
            if (version == _workflowPickerVersion)
            {
                WorkflowPickerLoading = false;
                NotifyStateChanged();
            }
        }
    }

    public async Task SelectWorkflowAsync(Guid workflowId)
    {
        if (NavigationLocked || !WorkflowPickerOpen || WorkflowPickerLoading
            || AvailableWorkflows.FirstOrDefault(w => w.Id == workflowId) is not { } workflow)
        {
            return;
        }
        await OpenPreviewAsync(new(workflow.Id, "workflow", workflow.Name));
    }

    public async Task PrepareWorkflowRunAsync()
    {
        if (!CanStartChat || !string.IsNullOrWhiteSpace(Draft) || PreviewLoading
            || PreviewError is not null || PreviewWorkflow is not { } workflow
            || workflow.ProjectId != Conversation.ProjectId)
        {
            return;
        }
        await StartDashboardActionAsync($"Help me run the existing workflow {System.Text.Json.JsonSerializer.Serialize(workflow.Name)} (ID: {workflow.Id}). Inspect its required inputs and ask me for any missing values before running it. Do not create or modify workflows.");
    }

    private void CloseWorkflowPicker()
    {
        ++_workflowPickerVersion;
        WorkflowPickerOpen = WorkflowPickerLoading = false;
        AvailableWorkflows = [];
        WorkflowPickerError = null;
    }
}
