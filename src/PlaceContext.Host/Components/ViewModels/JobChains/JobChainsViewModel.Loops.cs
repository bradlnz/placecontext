using PlaceContext.Domain.ValueObjects;
using PlaceContext.Application.Build;

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
    public Guid? LoopConnectionId { get; set; }
    public string? LoopModel { get; set; }
    public IReadOnlyList<ModelConnection> LoopConnections { get; private set; } = [];
    public IReadOnlyList<string> LoopModels => LoopConnections.FirstOrDefault(x => x.Id == LoopConnectionId)?.Models ?? [];
    public void SelectLoopConnection() => LoopModel = LoopModels.FirstOrDefault();
    public string? LoopError
    {
        get; private set;
    }
    private int _loopStageIndex;
    private bool _editingLoop;

    public async Task OpenLoopActionAt(string kind, int stageIndex)
    {
        LoopKind = kind;
        _loopStageIndex = Math.Clamp(stageIndex, 0, EdStages.Count);
        _editingLoop = false;
        LoopInstructions = "";
        LoopResponseType = LoopChainAction.TextResponse;
        LoopAgentProvider = kind == LoopChainAction.HumanType ? LoopChainAction.ExternalAgent : LoopChainAction.AutomaticAgent;
        LoopResponseField = kind == LoopChainAction.TaskType ? "task_result" : kind == LoopChainAction.HumanType ? "human_response" : "agent_response";
        LoopError = null;
        LoopConnectionId = null;
        LoopModel = null;
        if (kind != LoopChainAction.HumanType && _modelConnections is not null)
        {
            try
            {
                LoopConnections = (await _modelConnections.ListAsync()).Where(x => x.Enabled && x.Models.Count > 0).ToArray();
                LoopConnectionId = LoopConnections.FirstOrDefault()?.Id;
                SelectLoopConnection();
            }
            catch (Exception ex) { LoopError = ex.Message; }
        }
        ShowLoopEditor = true;
        NotifyStateChanged();
    }

    public async Task EditLoopAction(int stageIndex)
    {
        if (EdStageActions.GetValueOrDefault(stageIndex) is not LoopChainAction loop)
        {
            return;
        }
        await OpenLoopActionAt(loop.Type, stageIndex);
        _editingLoop = true;
        LoopInstructions = loop.Instructions;
        LoopResponseField = loop.ResponseField;
        LoopResponseType = loop.ResponseType;
        LoopAgentProvider = loop.AgentProvider;
        LoopConnectionId = loop.ConnectionId ?? LoopConnectionId;
        LoopModel = loop.Model ?? LoopModel;
    }

    public void SaveLoopAction()
    {
        LoopError = null;
        try
        {
            var action = new LoopChainAction(LoopKind, LoopInstructions, LoopResponseField, LoopResponseType, LoopAgentProvider,
                LoopAgentProvider == LoopChainAction.AutomaticAgent ? LoopConnectionId : null,
                LoopAgentProvider == LoopChainAction.AutomaticAgent ? LoopModel : null);
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
