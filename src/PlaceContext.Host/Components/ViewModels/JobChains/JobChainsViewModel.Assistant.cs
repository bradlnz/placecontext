using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public async Task RefreshAssistantCatalogueAsync()
    {
        var project = ProjectId;
        var workflows = await _svc.ListJobChainsAsync(project);
        if (ProjectId != project || ShowEditor) return;
        Chains = workflows;
        NotifyStateChanged();
    }

    public Task<string> AssistantContextAsync() => Task.FromResult(System.Text.Json.JsonSerializer.Serialize(new
    {
        ProjectId,
        workflows = Chains?.Take(50).Select(workflow => new { workflow.Id, workflow.Name, workflow.Description }),
        selectedWorkflow = AssistantWorkflow,
        activeView = EditorView,
        limits = "Up to 50 workflows and the selected saved workflow definition. Unsaved canvas edits are not included."
    }));

    public bool ShowWorkflowAssistant { get; private set; }

    public Task<string> QueryAssistantContextAsync() => Task.FromResult(System.Text.Json.JsonSerializer.Serialize(new
    {
        ProjectId, workflowId = EditChainId, dialect = "PostgreSQL", sql = QueryDataSql,
        QueryDataOutputField, schema = QueryDataTables.Take(50), QueryDataSchemaError,
        QueryDataPreviewError, limits = "Current query draft and up to 50 loaded table schemas. No query was executed for this question."
    }));

    public Task<string> LoopAssistantContextAsync() => Task.FromResult(System.Text.Json.JsonSerializer.Serialize(new
    {
        ProjectId, workflowId = EditChainId, LoopKind, LoopInstructions, LoopResponseType, LoopResponseField,
        LoopAgentProvider, LoopAgentId, LoopConnectionId, LoopModel,
        limits = "Current unsaved step settings. Changes have not been saved or executed."
    }));

    public Task<string> ExecutionAssistantContextAsync() => Task.FromResult(System.Text.Json.JsonSerializer.Serialize(new
    {
        ProjectId, workflowId = EditChainId, snapshotUtc = DateTimeOffset.UtcNow, step = ExecutionStep,
        limits = "Loaded execution details for the selected step only."
    }));

    public Task<string> ContainerAssistantContextAsync() => Task.FromResult(System.Text.Json.JsonSerializer.Serialize(new
    {
        ProjectId, workflowId = EditChainId, ContainerBuild, ContainerDockerfile, ContainerContextField,
        ContainerImage, ContainerImageField, ContainerCommand, ContainerOutputField,
        ContainerMemory, ContainerCpus, ContainerTimeout, ContainerNetwork, ContainerWeb, ContainerPort,
        limits = "Current unsaved container step settings. Nothing has been built or executed for this question."
    }));

    public Task<string> QemuAssistantContextAsync() => Task.FromResult(System.Text.Json.JsonSerializer.Serialize(new
    {
        ProjectId, workflowId = EditChainId, QemuCommand, QemuOutputField, QemuMemoryMb, QemuCpus,
        QemuTimeoutSeconds, QemuAllowNetwork, QemuPauseForLogin,
        limits = "Current unsaved browser workspace settings. No browser session was started for this question."
    }));

    public Task<string> QemuReplayAssistantContextAsync() => Task.FromResult(System.Text.Json.JsonSerializer.Serialize(new
    {
        ProjectId, workflowId = EditChainId, snapshotUtc = DateTimeOffset.UtcNow,
        step = QemuReplayRun?.Steps.FirstOrDefault(step => step.Index == QemuReplayStep),
        limits = "Loaded execution details for the selected browser workspace step only."
    }));
    public JobChainView? AssistantWorkflow => Chains?.FirstOrDefault(chain => chain.Id == EditChainId);

    public async Task OpenWorkflowAssistantAsync()
    {
        if (EditChainId is null) return;
        await SaveChainAsync(closeEditor: false);
        if (EditorError is not null || AssistantWorkflow is null) return;
        ShowWorkflowAssistant = true;
        NotifyStateChanged();
    }

    public async Task RefreshAssistantWorkflowAsync()
    {
        if (EditChainId is not { } id) return;
        try
        {
            Chains = await _svc.ListJobChainsAsync(ProjectId);
            Jobs = await _svc.ListJobsAsync(ProjectId);
            if (Chains.FirstOrDefault(chain => chain.Id == id) is { } saved)
                await OpenChainEditorAsync(saved, view: "canvas");
        }
        catch (Exception ex) { EditorError = ex.Message; }
        NotifyStateChanged();
    }

    public void CloseWorkflowAssistant()
    {
        ShowWorkflowAssistant = false;
        NotifyStateChanged();
    }
}
