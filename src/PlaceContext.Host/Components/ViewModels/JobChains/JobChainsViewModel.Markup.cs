using PlaceContext.Application.Dtos;
using PlaceContext.Host.Workflows;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public bool IsMarkupView => EditorView == "markup";
    public string MarkupText { get; set; } = "";
    public int MarkupRevision { get; private set; }
    public string? MarkupNotice { get; private set; }
    private string? _markupSource;
    private Guid? _markupWorkflowId;
    private string CurrentMarkup() => WorkflowMarkup.Write(new JobChainView(EditChainId ?? Guid.Empty, ProjectId,
        EdName, string.IsNullOrWhiteSpace(EdDescription) ? null : EdDescription,
        BuildCanvasStageViews(), _openedWorkflowUpdatedAt ?? DateTimeOffset.MinValue));

    private void PrepareMarkup()
    {
        if (_markupWorkflowId != EditChainId || _markupSource is null || MarkupText == _markupSource)
            ReloadMarkup();
    }

    public void ReloadMarkup()
    {
        MarkupText = _markupSource = CurrentMarkup();
        _markupWorkflowId = EditChainId;
        MarkupRevision++;
        MarkupNotice = null;
        EditorError = null;
        NotifyStateChanged();
    }

    public async Task DeployMarkupAsync(string yaml)
    {
        if (Saving || EditChainId is not { } id) return;
        MarkupText = yaml; EditorError = MarkupNotice = null; Saving = true;
        try
        {
            if (_markupSource != CurrentMarkup())
                throw new InvalidOperationException("The canvas changed after this YAML was loaded. Copy your edits, then reload the YAML before deploying.");
            var document = WorkflowMarkup.Read(yaml, id);
            var stages = document.Stages.Select(stage => stage.ToStage()).ToArray();
            var saved = await _svc.UpdateJobChainAsync(id, document.Name, document.Description, [],
                stages.Select(stage => stage.JobIds).ToArray(), stages.Select(stage => stage.Gate).ToArray(),
                stages.Select(stage => stage.Action).ToArray(), expectedUpdatedAt: _openedWorkflowUpdatedAt,
                stagePolicies: stages.Select(stage => stage.ExecutionPolicy).ToArray());
            Chains = (Chains ?? []).Select(chain => chain.Id == id ? saved : chain).ToArray();
            _markupSource = null;
            await OpenChainEditorAsync(saved, view: "markup");
            Message = MarkupNotice = $"Workflow '{saved.Name}' deployed.";
        }
        catch (Exception ex) { EditorError = ex.Message; }
        finally { Saving = false; NotifyStateChanged(); }
    }
}
