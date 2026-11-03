using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public bool ShowLoopEditor
    {
        get; private set;
    }
    public string LoopKind { get; private set; } = LoopChainAction.HumanType;
    public string LoopInstructions { get; set; } = "";
    public string LoopResponseType { get; set; } = LoopChainAction.TextResponse;
    public string LoopResponseField { get; set; } = "response";
    public string LoopAgentProvider { get; set; } = LoopChainAction.ExternalAgent;
    public string? LoopError
    {
        get; private set;
    }
    private int _loopStageIndex;
    private bool _editingLoop;

    public void OpenLoopActionAt(string kind, int stageIndex)
    {
        LoopKind = kind;
        _loopStageIndex = Math.Clamp(stageIndex, 0, EdStages.Count);
        _editingLoop = false;
        LoopInstructions = "";
        LoopResponseType = LoopChainAction.TextResponse;
        LoopAgentProvider = LoopChainAction.ExternalAgent;
        LoopResponseField = kind == LoopChainAction.HumanType ? "human_response" : "agent_response";
        LoopError = null;
        ShowLoopEditor = true;
        NotifyStateChanged();
    }

    public void EditLoopAction(int stageIndex)
    {
        if (EdStageActions.GetValueOrDefault(stageIndex) is not LoopChainAction loop)
        {
            return;
        }
        OpenLoopActionAt(loop.Type, stageIndex);
        _editingLoop = true;
        LoopInstructions = loop.Instructions;
        LoopResponseField = loop.ResponseField;
        LoopResponseType = loop.ResponseType;
        LoopAgentProvider = loop.AgentProvider;
    }

    public void SaveLoopAction()
    {
        LoopError = null;
        try
        {
            var action = new LoopChainAction(LoopKind, LoopInstructions, LoopResponseField, LoopResponseType, LoopAgentProvider);
            if (!_editingLoop)
            {
                ShiftGatesForInsert(_loopStageIndex);
                ShiftActionsForInsert(_loopStageIndex);
                EdStages.Insert(_loopStageIndex, []);
            }
            EdStageActions[_loopStageIndex] = action;
            ShowLoopEditor = false;
        }
        catch (ArgumentException ex)
        {
            LoopError = ex.Message;
        }
        NotifyStateChanged();
    }

    public void CloseLoopEditor()
    {
        ShowLoopEditor = false;
        LoopError = null;
        NotifyStateChanged();
    }
}
