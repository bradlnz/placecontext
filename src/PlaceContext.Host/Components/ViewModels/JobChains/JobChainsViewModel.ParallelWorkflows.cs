using System.Text.Json;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;
namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public sealed class ParallelBranchEditor
    {
        public string WorkflowId { get; set; } = "";
        public bool WhenFalse { get; set; }
        public string DraftName { get; set; } = "";
        public IReadOnlyList<ChainStage>? DraftStages { get; set; }
        public string Match { get; set; } = "all";
        public List<ConditionRow> Conditions { get; } = [new()];
        public string Expression => Conditions.Count == 1 ? Conditions[0].Expression : $"{Match}:{JsonSerializer.Serialize(Conditions.Select(row => row.Expression))}";
    }
    public bool ShowParallelWorkflowEditor { get; private set; }
    public List<ParallelBranchEditor> ParallelBranches { get; } = [];
    public ParallelBranchEditor? ActiveParallelBranch { get; private set; }
    public string ParallelOutputField { get; set; } = "parallel_results";
    public string? ParallelWorkflowError { get; private set; }
    private int _parallelStageIndex;
    public Task<ConditionFieldOptions> LoadParallelConditionFieldsAsync() => LoadConditionFieldsAsync(_parallelStageIndex);
    private bool _editingParallel;
    private bool _replacingStep;
    public bool SavingParallelWorkflows { get; private set; }
    public bool CanBranchStep(int index) => index >= 0 && index < EdStages.Count
        && (EdStages[index].Count > 0 || EdStageActions.GetValueOrDefault(index) is not null)
        && EdStageGates.GetValueOrDefault(index) is not WaitGate
        && EdStageActions.GetValueOrDefault(index) is not RepeatFlowChainAction
        && EdStageActions.GetValueOrDefault(index) is not VirtualMachineChainAction { PauseForLogin: true }
        && EdStageActions.GetValueOrDefault(index) is not LoopChainAction { AgentProvider: not (LoopChainAction.AutomaticAgent or LoopChainAction.JevAgent) };
    public void OpenStepPathways(int index)
    {
        var next = index + 1;
        OpenParallelWorkflowAction(next, EdStageActions.GetValueOrDefault(next) is ParallelWorkflowsChainAction);
    }
    public void OpenStepBranches(int index)
    {
        if (!CanBranchStep(index)) { OpenStepPathways(index); return; }
        if (EdStageActions.GetValueOrDefault(index) is ParallelWorkflowsChainAction)
        {
            OpenParallelWorkflowAction(index, true);
            AddParallelBranch();
            return;
        }
        OpenParallelWorkflowAction(index, true);
        _replacingStep = true;
        ParallelBranches.Clear();
        var gate = EdStageGates.GetValueOrDefault(index);
        var action = EdStageActions.GetValueOrDefault(index);
        var policy = EdStagePolicies.GetValueOrDefault(index);
        if (gate is ConditionGate { ReviewQuestion: null } condition)
        {
            ParallelBranches.Add(DraftBranch("True path", [new(EdStages[index], action: action, executionPolicy: policy)], condition.Expression));
            if (condition.ElseBranch is { Count: > 0 } other)
            {
                var otherwise = DraftBranch("False path", other, condition.Expression);
                otherwise.WhenFalse = true;
                ParallelBranches.Add(otherwise);
            }
        }
        else
        {
            ParallelBranches.Add(DraftBranch("Original step", [new(EdStages[index], gate, action: action, executionPolicy: policy)], "true"));
        }
        ParallelBranches.Add(new());
        ActiveParallelBranch = ParallelBranches[^1];
        NotifyStateChanged();
    }
    private ParallelBranchEditor DraftBranch(string name, IReadOnlyList<ChainStage> stages, string condition)
    {
        var branch = new ParallelBranchEditor { DraftName = $"{EdName} · {name}", DraftStages = stages };
        branch.Conditions.Clear();
        branch.Conditions.Add(ConditionRow.Parse(condition));
        return branch;
    }
    public JobChainView? ParallelBranchWorkflow(ParallelBranchEditor branch) =>
        Chains?.FirstOrDefault(chain => chain.Id.ToString() == branch.WorkflowId && chain.Id != EditChainId);

    public async Task SelectSavedParallelWorkflowAsync(ParallelBranchEditor branch, Guid id)
    {
        branch.WorkflowId = id.ToString();
        branch.DraftStages = null;
        try
        {
            Chains = await _svc.ListJobChainsAsync(ProjectId);
            Jobs = await _svc.ListJobsAsync(ProjectId);
            ParallelWorkflowError = null;
        }
        catch (Exception ex) { ParallelWorkflowError = ex.Message; }
        NotifyStateChanged();
    }
    public void OpenParallelWorkflowAction(int index, bool editing = false)
    {
        _parallelStageIndex = index; _editingParallel = editing; _replacingStep = false;
        ParallelBranches.Clear(); ParallelWorkflowError = null;
        var action = editing ? EdStageActions.GetValueOrDefault(index) as ParallelWorkflowsChainAction : null;
        ParallelOutputField = action?.OutputField ?? "parallel_results";
        foreach (var branch in action?.Branches ?? [])
        {
            var row = new ParallelBranchEditor { WorkflowId = branch.WorkflowId.ToString(), WhenFalse = branch.WhenFalse };
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
        ActiveParallelBranch = ParallelBranches[0];
        ShowParallelWorkflowEditor = true; NotifyStateChanged();
    }
    public void SelectParallelBranch(ParallelBranchEditor branch)
    {
        if (!ParallelBranches.Contains(branch)) return;
        ActiveParallelBranch = branch;
        NotifyStateChanged();
    }
    public void OpenParallelBranch(int stageIndex, int branchIndex)
    {
        OpenParallelWorkflowAction(stageIndex, true);
        SelectParallelBranch(ParallelBranches[branchIndex]);
    }
    public void AddParallelBranch()
    {
        if (ParallelBranches.Count >= 8) return;
        ActiveParallelBranch = new();
        ParallelBranches.Add(ActiveParallelBranch);
        NotifyStateChanged();
    }
    public void RemoveParallelBranch(ParallelBranchEditor branch)
    {
        var index = ParallelBranches.IndexOf(branch);
        if (ParallelBranches.Count <= 1 || index < 0) return;
        ParallelBranches.RemoveAt(index);
        if (ActiveParallelBranch == branch)
            ActiveParallelBranch = ParallelBranches[Math.Min(index, ParallelBranches.Count - 1)];
        NotifyStateChanged();
    }
    public void DeleteWorkflowBranch(int stageIndex, int branchIndex)
    {
        if (EdStageActions.GetValueOrDefault(stageIndex) is not ParallelWorkflowsChainAction action
            || branchIndex < 0 || branchIndex >= action.Branches.Count) return;
        if (action.Branches.Count == 1)
        {
            RemoveStage(stageIndex);
            return;
        }
        EdStageActions[stageIndex] = new ParallelWorkflowsChainAction(
            action.Branches.Where((_, index) => index != branchIndex).ToArray(), action.OutputField);
        NotifyStateChanged();
    }
    public void CloseParallelWorkflowEditor() { if (SavingParallelWorkflows) return; ShowParallelWorkflowEditor = false; NotifyStateChanged(); }
    public async Task SaveParallelWorkflowActionAsync()
    {
        if (SavingParallelWorkflows) return;
        ParallelWorkflowError = null;
        try
        {
            // Validate the whole draft before creating any branch workflows.
            _ = new ParallelWorkflowsChainAction(ParallelBranches.Select(branch =>
            {
                var workflow = ParallelBranchWorkflow(branch);
                if (workflow is null && branch.DraftStages is null)
                    throw new ArgumentException("Choose a workflow or add actions for every branch.");
                return new ConditionalWorkflowBranch(workflow?.Id ?? Guid.NewGuid(), workflow?.Name ?? branch.DraftName, branch.Expression, branch.WhenFalse);
            }).ToArray(), ParallelOutputField);
            SavingParallelWorkflows = true;
            NotifyStateChanged();
            foreach (var branch in ParallelBranches.Where(branch => string.IsNullOrEmpty(branch.WorkflowId) && branch.DraftStages is not null))
            {
                var stages = branch.DraftStages!;
                var created = await _svc.CreateJobChainAsync(ProjectId, branch.DraftName, null,
                    stages.SelectMany(stage => stage.JobIds).ToArray(), stages.Select(stage => stage.JobIds).ToArray(),
                    stages.Select(stage => stage.Gate is ConditionGate condition && stage.ElseBranch is not null
                        ? condition with { ElseBranch = stage.ElseBranch } : stage.Gate).ToArray(), stages.Select(stage => stage.Action).ToArray(),
                    stagePolicies: stages.Select(stage => stage.ExecutionPolicy).ToArray());
                branch.WorkflowId = created.Id.ToString();
                branch.DraftStages = null;
                Chains = (Chains ?? []).Append(created).ToArray();
            }
            var branches = ParallelBranches.Select(branch =>
            {
                var workflow = Chains?.FirstOrDefault(chain => chain.Id.ToString() == branch.WorkflowId && chain.Id != EditChainId)
                    ?? throw new ArgumentException("Choose a workflow for every branch.");
                return new ConditionalWorkflowBranch(workflow.Id, workflow.Name, branch.Expression, branch.WhenFalse);
            }).ToArray();
            var action = new ParallelWorkflowsChainAction(branches, ParallelOutputField);
            if (!_editingParallel)
            {
                ShiftGatesForInsert(_parallelStageIndex); ShiftActionsForInsert(_parallelStageIndex); EdStages.Insert(_parallelStageIndex, []);
            }
            if (_replacingStep)
            {
                EdStages[_parallelStageIndex] = [];
                EdStageGates.Remove(_parallelStageIndex);
                EdStagePolicies.Remove(_parallelStageIndex);
            }
            EdStageActions[_parallelStageIndex] = action;
            ShowParallelWorkflowEditor = false;
        }
        catch (Exception ex) { ParallelWorkflowError = ex.Message; }
        finally { SavingParallelWorkflows = false; NotifyStateChanged(); }
    }
}
