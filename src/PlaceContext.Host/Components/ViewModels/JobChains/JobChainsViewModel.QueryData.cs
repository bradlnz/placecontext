using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public bool ShowQueryDataEditor { get; private set; }
    public string QueryDataSql { get; set; } = "";
    public string QueryDataOutputField { get; set; } = "query_data";
    public string? QueryDataError { get; private set; }
    private int _queryDataStageIndex;
    private bool _editingQueryData;

    public void OpenQueryDataAction(int stageIndex, bool editing = false)
    {
        _queryDataStageIndex = Math.Clamp(stageIndex, 0, EdStages.Count); _editingQueryData = editing;
        var action = editing ? EdStageActions.GetValueOrDefault(stageIndex) as QueryDataChainAction : null;
        QueryDataSql = action?.Sql ?? ""; QueryDataOutputField = action?.OutputField ?? "query_data";
        QueryDataError = null; ShowQueryDataEditor = true; NotifyStateChanged();
    }
    public void CloseQueryDataEditor() { ShowQueryDataEditor = false; NotifyStateChanged(); }
    public void SaveQueryDataAction()
    {
        try
        {
            var action = new QueryDataChainAction(QueryDataSql, QueryDataOutputField);
            if (!_editingQueryData)
            {
                ShiftGatesForInsert(_queryDataStageIndex); ShiftActionsForInsert(_queryDataStageIndex); EdStages.Insert(_queryDataStageIndex, []);
            }
            EdStageActions[_queryDataStageIndex] = action; ShowQueryDataEditor = false; QueryDataError = null;
        }
        catch (ArgumentException ex) { QueryDataError = ex.Message; }
        NotifyStateChanged();
    }
}
