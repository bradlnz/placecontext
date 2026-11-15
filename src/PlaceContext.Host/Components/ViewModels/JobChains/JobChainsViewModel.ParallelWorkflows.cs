using System.Text.Json;
using PlaceContext.Domain.ValueObjects;
namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public sealed class ParallelBranchEditor
    {
        public string WorkflowId { get; set; } = "";
        public string Match { get; set; } = "all";
        public List<ConditionRow> Conditions { get; } = [new()];
        public string Expression => Conditions.Count == 1 ? Conditions[0].Expression : $"{Match}:{JsonSerializer.Serialize(Conditions.Select(row => row.Expression))}";
    }
    public bool ShowParallelWorkflowEditor { get; private set; }
    public List<ParallelBranchEditor> ParallelBranches { get; } = [];
    public string ParallelOutputField { get; set; } = "parallel_results";
    public string? ParallelWorkflowError { get; private set; }
    private int _parallelStageIndex;
    private bool _editingParallel;
    public void OpenParallelWorkflowAction(int index, bool editing = false)
    {
        _parallelStageIndex = index; _editingParallel = editing;
        ParallelBranches.Clear(); ParallelWorkflowError = null;
        var action = editing ? EdStageActions.GetValueOrDefault(index) as ParallelWorkflowsChainAction : null;
        ParallelOutputField = action?.OutputField ?? "parallel_results";
        foreach (var branch in action?.Branches ?? [])
        {
            var row = new ParallelBranchEditor { WorkflowId = branch.WorkflowId.ToString() };
            row.Conditions.Clear();
            if (branch.Condition.StartsWith("all:") || branch.Condition.StartsWith("any:"))
            {
                try
                {
                    var values = JsonSerializer.Deserialize<string[]>(branch.Condition[4..]);
                    if (values is { Length: > 0 } && values.All(value => value is not null))
                    { row.Match = branch.Condition[..3]; row.Conditions.AddRange(values.Select(ConditionRow.Parse)); }
                }
                catch (JsonException) { }
            }
            if (row.Conditions.Count == 0) row.Conditions.Add(ConditionRow.Parse(branch.Condition));
            ParallelBranches.Add(row);
        }
        if (ParallelBranches.Count == 0) { ParallelBranches.Add(new()); ParallelBranches.Add(new()); }
        ShowParallelWorkflowEditor = true; NotifyStateChanged();
    }
    public void AddParallelBranch() { if (ParallelBranches.Count < 8) ParallelBranches.Add(new()); NotifyStateChanged(); }
    public void RemoveParallelBranch(ParallelBranchEditor branch) { if (ParallelBranches.Count > 2) ParallelBranches.Remove(branch); NotifyStateChanged(); }
    public void CloseParallelWorkflowEditor() { ShowParallelWorkflowEditor = false; NotifyStateChanged(); }
    public void SaveParallelWorkflowAction()
    {
        try
        {
            var branches = ParallelBranches.Select(branch =>
            {
                var workflow = Chains?.FirstOrDefault(chain => chain.Id.ToString() == branch.WorkflowId && chain.Id != EditChainId)
                    ?? throw new ArgumentException("Choose a workflow for every branch.");
                return new ConditionalWorkflowBranch(workflow.Id, workflow.Name, branch.Expression);
            }).ToArray();
            var action = new ParallelWorkflowsChainAction(branches, ParallelOutputField);
            if (!_editingParallel)
            {
                ShiftGatesForInsert(_parallelStageIndex); ShiftActionsForInsert(_parallelStageIndex); EdStages.Insert(_parallelStageIndex, []);
            }
            EdStageActions[_parallelStageIndex] = action; CloseParallelWorkflowEditor();
        }
        catch (ArgumentException ex) { ParallelWorkflowError = ex.Message; NotifyStateChanged(); }
    }
}
