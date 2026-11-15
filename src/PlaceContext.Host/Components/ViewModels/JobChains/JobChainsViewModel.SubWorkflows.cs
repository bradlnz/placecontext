using PlaceContext.Domain.ValueObjects;
namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public bool ShowSubWorkflowEditor { get; private set; }
    public string SubWorkflowId { get; set; } = "";
    public string? SubWorkflowError { get; private set; }
    private int _subWorkflowStageIndex;
    private bool _editingSubWorkflow;
    public void OpenSubWorkflowAction(int index, bool editing = false)
    {
        _subWorkflowStageIndex = index;
        _editingSubWorkflow = editing;
        SubWorkflowId = editing && EdStageActions.GetValueOrDefault(index) is SubWorkflowChainAction child ? child.WorkflowId.ToString() : "";
        SubWorkflowError = null;
        ShowSubWorkflowEditor = true;
        NotifyStateChanged();
    }
    public void CloseSubWorkflowEditor() { ShowSubWorkflowEditor = false; NotifyStateChanged(); }
    public void SaveSubWorkflowAction()
    {
        var workflow = Chains?.FirstOrDefault(chain => chain.Id.ToString() == SubWorkflowId && chain.Id != EditChainId);
        if (workflow is null) { SubWorkflowError = "Choose another workflow in this project."; NotifyStateChanged(); return; }
        if (!_editingSubWorkflow)
        {
            ShiftGatesForInsert(_subWorkflowStageIndex);
            ShiftActionsForInsert(_subWorkflowStageIndex);
            EdStages.Insert(_subWorkflowStageIndex, []);
        }
        EdStageActions[_subWorkflowStageIndex] = new SubWorkflowChainAction(workflow.Id, workflow.Name);
        CloseSubWorkflowEditor();
    }
}
