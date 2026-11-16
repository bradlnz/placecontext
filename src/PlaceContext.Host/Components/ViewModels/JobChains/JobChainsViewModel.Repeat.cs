using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public bool ShowRepeatEditor { get; private set; }
    public string RepeatCondition { get; set; } = "eq:readiness.production_ready:true";
    public int RepeatMaxAttempts { get; set; } = 3;
    public string? RepeatError { get; private set; }
    private int? _repeatStageIndex;
    private bool _insertRepeat;

    public void OpenRepeatAction(int? stageIndex = null)
    {
        _insertRepeat = false;
        _repeatStageIndex = stageIndex;
        var action = stageIndex is { } index ? EdStageActions.GetValueOrDefault(index) as RepeatFlowChainAction : null;
        RepeatCondition = action?.UntilCondition ?? "eq:readiness.production_ready:true";
        RepeatMaxAttempts = action?.MaxAttempts ?? 3;
        RepeatError = null;
        ShowRepeatEditor = true;
        NotifyStateChanged();
    }

    public bool CanRetryGate => !IsAutomaticGateEditor && GateEditorStageIndex > 0 && !EdStageActions.Values.Any(action => action is RepeatFlowChainAction);

    public void RetryGateProcess()
    {
        if (!CanRetryGate) return;
        var condition = BuildConditionExpression();
        SaveGate();
        OpenRepeatAction(GateEditorStageIndex);
        RepeatCondition = condition;
        _insertRepeat = true;
        ShowGateEditor = false;
        NotifyStateChanged();
    }

    public void SaveRepeatAction()
    {
        try
        {
            var action = new RepeatFlowChainAction(RepeatCondition, RepeatMaxAttempts);
            if (_repeatStageIndex is null || _insertRepeat)
            {
                if (EdStages.Count == 0 || EdStageActions.Values.Any(value => value is RepeatFlowChainAction))
                    throw new ArgumentException("Add steps first. A workflow can have one Repeat flow step.");
                _repeatStageIndex ??= EdStages.Count;
                ShiftGatesForInsert(_repeatStageIndex.Value);
                ShiftActionsForInsert(_repeatStageIndex.Value);
                EdStages.Insert(_repeatStageIndex.Value, []);
            }
            EdStageActions[_repeatStageIndex.Value] = action;
            ShowRepeatEditor = false;
            RepeatError = null;
        }
        catch (ArgumentException ex) { RepeatError = ex.Message; }
        NotifyStateChanged();
    }

    public void CloseRepeatEditor()
    {
        ShowRepeatEditor = false;
        NotifyStateChanged();
    }
}
