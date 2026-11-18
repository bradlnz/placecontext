using PlaceContext.Application.Dtos;
using PlaceContext.Host.Workflows;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public string MarkupText { get; private set; } = "";
    public string? MarkupError { get; private set; }
    private string? _markupSource;
    private string CurrentMarkup() => WorkflowMarkup.Write(new JobChainView(EditChainId ?? Guid.Empty, ProjectId,
        EdName, string.IsNullOrWhiteSpace(EdDescription) ? null : EdDescription,
        BuildCanvasStageViews(), _openedWorkflowUpdatedAt ?? DateTimeOffset.MinValue));

    public void ReloadMarkup()
    {
        MarkupText = _markupSource = CurrentMarkup();
        MarkupError = null;
        NotifyStateChanged();
    }

    public void SyncMarkupFromCanvas()
    {
        if (MarkupError is null && _markupSource != CurrentMarkup()) ReloadMarkup();
    }

    public bool ApplyMarkup(string yaml)
    {
        if (Saving) return false;
        if (yaml == MarkupText && MarkupError is null) return true;
        MarkupText = yaml;
        try
        {
            if (_markupSource != CurrentMarkup())
                throw new InvalidOperationException("The canvas changed while the YAML draft was invalid. Copy your edits, then use Reset YAML to load the current canvas.");
            var document = WorkflowMarkup.Read(yaml, EditChainId ?? Guid.Empty);
            var stages = document.Stages.Select(stage => stage.ToStage()).ToArray();
            EdName = document.Name;
            EdDescription = document.Description ?? "";
            EdStages.Clear(); EdStageGates.Clear(); EdStageActions.Clear(); EdStagePolicies.Clear(); EdBranchJobIds.Clear();
            foreach (var (stage, index) in stages.Select((stage, index) => (stage, index)))
            {
                EdStages.Add(stage.JobIds.ToList());
                EdStageGates[index] = stage.Gate;
                EdStageActions[index] = stage.Action;
                EdStagePolicies[index] = stage.ExecutionPolicy;
            }
            Readiness = null;
            _markupSource = CurrentMarkup();
            MarkupError = null;
        }
        catch (Exception ex) { MarkupError = ex.Message; }
        NotifyStateChanged();
        return MarkupError is null;
    }
}
