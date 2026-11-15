using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public bool ShowWorkflowAssistant { get; private set; }
    public JobChainView? AssistantWorkflow => Chains?.FirstOrDefault(chain => chain.Id == EditChainId);

    public async Task OpenWorkflowAssistantAsync()
    {
        if (EditChainId is not { } id) return;
        await SaveChainAsync();
        if (EditorError is not null || Chains?.FirstOrDefault(chain => chain.Id == id) is not { } saved) return;
        await OpenChainEditorAsync(saved, view: "canvas");
        ShowWorkflowAssistant = true;
        NotifyStateChanged();
    }

    public async Task RefreshAssistantWorkflowAsync()
    {
        if (EditChainId is not { } id) return;
        try
        {
            Chains = await _svc.ListJobChainsAsync(ProjectId);
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
