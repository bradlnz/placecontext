using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Build;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class BuildChatViewModel
{
    public bool WorkflowPickerOpen { get; private set; }
    public bool WorkflowPickerLoading { get; private set; }
    public string? WorkflowPickerError { get; private set; }
    public bool WorkflowPickerRestricted { get; private set; }
    public string WorkflowPickerAgentName { get; private set; } = "";
    public bool WorkflowSelectedForRun { get; private set; }
    public bool PreparingWorkflowRun { get; private set; }
    public JobChainsViewModel? WorkflowRunPrompt { get; private set; }
    public string? WorkflowRunError { get; private set; }
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
        WorkflowPickerRestricted = false;
        WorkflowPickerAgentName = "";
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
            var agentId = Conversation.AgentId ?? DashboardSettings.DefaultAgentId;
            var selectedAgent = AvailableAgents.FirstOrDefault(a => a.Id == agentId);
            if (selectedAgent is null || selectedAgent.WorkflowAccess is not ("run" or "manage"))
            {
                if (version == _workflowPickerVersion)
                {
                    WorkflowPickerRestricted = true;
                    WorkflowPickerAgentName = selectedAgent?.DisplayName ?? "";
                    AvailableWorkflows = [];
                }
                return;
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
        WorkflowSelectedForRun = true;
    }

    public async Task PrepareWorkflowRunAsync()
    {
        if (NavigationLocked || PreparingWorkflowRun || PreviewLoading
            || PreviewError is not null || PreviewWorkflow is not { } workflow
            || workflow.ProjectId != Conversation.ProjectId)
        {
            return;
        }
        var version = _previewVersion;
        PreparingWorkflowRun = true;
        WorkflowRunError = null;
        NotifyStateChanged();
        try
        {
            if (!await permissions.HasAsync(Permission.ProjectsView) || !await permissions.HasAsync(Permission.ChainsManage)
                || !await permissions.HasAsync(Permission.JobsRun))
                throw new UnauthorizedAccessException("Permission to run workflows is required.");
            var selectedAgent = AvailableAgents.FirstOrDefault(a => a.Id == (Conversation.AgentId ?? DashboardSettings.DefaultAgentId));
            if (selectedAgent?.WorkflowAccess is not ("run" or "manage"))
                throw new UnauthorizedAccessException("The selected agent does not have workflow run access.");
            var jobs = await service.ListJobsAsync(workflow.ProjectId);
            if (version != _previewVersion) return;
            var prompt = new JobChainsViewModel(service, operations!, permissions, navigation!, submissions);
            prompt.Initialize(workflow.ProjectId);
            await prompt.RunChainAsync(workflow, jobs);
            WorkflowRunPrompt = prompt;
        }
        catch (Exception ex)
        {
            if (version == _previewVersion) WorkflowRunError = ex.Message;
        }
        finally
        {
            if (version == _previewVersion) PreparingWorkflowRun = false;
            NotifyStateChanged();
        }
    }

    private void CloseWorkflowPicker()
    {
        ++_workflowPickerVersion;
        WorkflowPickerOpen = WorkflowPickerLoading = false;
        WorkflowPickerRestricted = false;
        WorkflowPickerAgentName = "";
        AvailableWorkflows = [];
        WorkflowPickerError = null;
    }
}
