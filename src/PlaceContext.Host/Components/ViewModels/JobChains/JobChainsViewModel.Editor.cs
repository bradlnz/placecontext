using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels.Helpers;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    // ── Editor state ──────────────────────────────────────────────────────────────────────────
    public bool ShowEditor { get; private set; }
    public bool Saving { get; private set; }
    public Guid? EditChainId { get; private set; }
    public string? EditorError { get; private set; }
    public string EditorTab { get; set; } = "details";
    public string EditorView { get; set; } = "canvas";
    public string EdName { get; set; } = "";
    public string EdDescription { get; set; } = "";
    public List<List<Guid>> EdStages { get; } = new();
    public string EdAddJobId { get; set; } = "";

    /// <summary>Gates keyed by stage index (the stage after the gate). null = no gate.</summary>
    public Dictionary<int, ChainGate?> EdStageGates { get; } = new();

    // ── Gate editor modal state ───────────────────────────────────────────────────────────────
    public bool ShowGateEditor { get; private set; }
    public int GateEditorStageIndex { get; private set; }
    public string GateEditorType { get; set; } = "wait"; // "wait" or "condition"
    public double GateEditorDuration { get; set; } = 30;
    public string GateEditorExpression { get; set; } = "exists:data";
    public string GateEditorOperator { get; set; } = "exists";
    public string GateEditorPath { get; set; } = "data";
    public string GateEditorValue { get; set; } = "";

    // ── Editor ────────────────────────────────────────────────────────────────────────────────
    public void NewChain()
    {
        EditChainId = null;
        EdName = "";
        EdDescription = "";
        EdStages.Clear();
        EdAddJobId = "";
        EdBranchJobIds.Clear();
        EdStageGates.Clear();
        EditorError = null;
        EditorTab = "details";
        EditorView = "canvas";
        ShowEditor = true;
        NotifyStateChanged();
    }

    public async Task OpenChainAsync(JobChainView chain)
    {
        OpenChainEditor(chain);
        await SwitchToRunsTabAsync();
    }

    public void OpenChainEditor(JobChainView chain)
    {
        EditChainId = chain.Id;
        EdName = chain.Name;
        EdDescription = chain.Description ?? "";
        EdStages.Clear();
        EdStageGates.Clear();
        foreach (var stage in chain.Stages)
            EdStages.Add(stage.Jobs.Select(j => j.JobId).ToList());
        foreach (var (stage, i) in chain.Stages.Select((s, i) => (s, i)))
        {
            EdStageGates[i] = FromViewGate(stage.Gate);
        }
        EdAddJobId = "";
        EdBranchJobIds.Clear();
        EditorError = null;
        EditorTab = "details";
        EditorView = "canvas";
        OpenRun = null;
        StepRunDetail = null;
        ShowEditor = true;
        NotifyStateChanged();
    }

    public void CloseEditor()
    {
        ShowEditor = false;
        ShowGateEditor = false;
        EditorError = null;
        OpenRun = null;
        StepRunDetail = null;
        StopPolling();
        NotifyStateChanged();
    }

    public void AddStage()
    {
        if (!Guid.TryParse(EdAddJobId, out var jobId)) return;
        EdStages.Add(new List<Guid> { jobId });
        EdAddJobId = "";
        NotifyStateChanged();
    }

    public void AddStage(Guid jobId)
    {
        if (jobId == Guid.Empty) return;
        EdStages.Add(new List<Guid> { jobId });
        NotifyStateChanged();
    }

    public Dictionary<int, string> EdBranchJobIds { get; } = new();

    public void AddBranch(int stageIndex)
    {
        if (stageIndex >= 0 && stageIndex < EdStages.Count
            && EdBranchJobIds.TryGetValue(stageIndex, out var selectedId)
            && Guid.TryParse(selectedId, out var jobId))
        {
            EdStages[stageIndex].Add(jobId);
            EdBranchJobIds.Remove(stageIndex);
            NotifyStateChanged();
        }
    }

    public void RemoveBranch(int stageIndex, int branchIndex)
    {
        if (stageIndex >= 0 && stageIndex < EdStages.Count
            && branchIndex >= 0 && branchIndex < EdStages[stageIndex].Count)
        {
            EdStages[stageIndex].RemoveAt(branchIndex);
            if (EdStages[stageIndex].Count == 0)
                RemoveStage(stageIndex);
            else
                NotifyStateChanged();
        }
    }

    public void MoveStage(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= EdStages.Count) return;
        (EdStages[index], EdStages[target]) = (EdStages[target], EdStages[index]);
        var sourceGate = EdStageGates.GetValueOrDefault(index);
        var targetGate = EdStageGates.GetValueOrDefault(target);
        SetGate(index, targetGate);
        SetGate(target, sourceGate);
        NotifyStateChanged();
    }

    public void MoveStageTo(int sourceIndex, int targetIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= EdStages.Count
            || targetIndex < 0 || targetIndex >= EdStages.Count
            || sourceIndex == targetIndex) return;

        var stage = EdStages[sourceIndex];
        var gate = EdStageGates.GetValueOrDefault(sourceIndex);
        EdStages.RemoveAt(sourceIndex);
        ShiftGatesAfterRemoval(sourceIndex);
        if (targetIndex > sourceIndex) targetIndex--;
        EdStages.Insert(targetIndex, stage);
        ShiftGatesForInsert(targetIndex);
        SetGate(targetIndex, gate);
        NotifyStateChanged();
    }

    public void RemoveStage(int stageIndex)
    {
        if (stageIndex < 0 || stageIndex >= EdStages.Count) return;
        EdStages.RemoveAt(stageIndex);
        ShiftGatesAfterRemoval(stageIndex);
        NotifyStateChanged();
    }

    public void AddPath(int stageIndex, Guid jobId)
    {
        if (stageIndex < 0 || stageIndex >= EdStages.Count || jobId == Guid.Empty) return;
        EdStages[stageIndex].Add(jobId);
        NotifyStateChanged();
    }

    public void MovePath(int sourceStage, int sourceBranch, int targetStage)
    {
        if (sourceStage < 0 || sourceStage >= EdStages.Count
            || targetStage < 0 || targetStage >= EdStages.Count
            || sourceBranch < 0 || sourceBranch >= EdStages[sourceStage].Count
            || sourceStage == targetStage) return;

        var jobId = EdStages[sourceStage][sourceBranch];
        EdStages[sourceStage].RemoveAt(sourceBranch);
        EdStages[targetStage].Add(jobId);
        if (EdStages[sourceStage].Count == 0)
            RemoveStage(sourceStage);
        else
            NotifyStateChanged();
    }

    private void ShiftGatesAfterRemoval(int removedIndex)
    {
        var shifted = EdStageGates
            .Where(kv => kv.Key != removedIndex)
            .ToDictionary(kv => kv.Key > removedIndex ? kv.Key - 1 : kv.Key, kv => kv.Value);
        EdStageGates.Clear();
        foreach (var (key, value) in shifted) EdStageGates[key] = value;
    }

    private void ShiftGatesForInsert(int insertedIndex)
    {
        var shifted = EdStageGates
            .OrderByDescending(kv => kv.Key)
            .ToList();
        EdStageGates.Clear();
        foreach (var (key, value) in shifted)
            EdStageGates[key >= insertedIndex ? key + 1 : key] = value;
    }

    private void SetGate(int index, ChainGate? gate)
    {
        if (gate is null) EdStageGates.Remove(index);
        else EdStageGates[index] = gate;
    }

    // ── Gate editing ──────────────────────────────────────────────────────────────────────────

    /// <summary>Open the gate editor for the stage after the gate at the given source index.</summary>
    public void OpenGateEditor(int stageIndex)
    {
        GateEditorStageIndex = stageIndex;
        var existing = EdStageGates.GetValueOrDefault(stageIndex);
        switch (existing)
        {
            case WaitGate w:
                GateEditorType = "wait";
                GateEditorDuration = w.Duration.TotalSeconds;
                GateEditorExpression = "exists:data";
                GateEditorOperator = "exists";
                GateEditorPath = "data";
                GateEditorValue = "";
                break;
            case ConditionGate c:
                GateEditorType = "condition";
                GateEditorExpression = c.Expression;
                ParseCondition(c.Expression);
                GateEditorDuration = 30;
                break;
            default:
                GateEditorType = "wait";
                GateEditorDuration = 30;
                GateEditorExpression = "exists:data";
                GateEditorOperator = "exists";
                GateEditorPath = "data";
                GateEditorValue = "";
                break;
        }
        ShowGateEditor = true;
        NotifyStateChanged();
    }

    public void SaveGate()
    {
        if (GateEditorType == "condition")
            GateEditorExpression = BuildConditionExpression();
        EdStageGates[GateEditorStageIndex] = GateEditorType switch
        {
            "wait" => new WaitGate(TimeSpan.FromSeconds(GateEditorDuration)),
            "condition" => new ConditionGate(GateEditorExpression),
            _ => null,
        };
        ShowGateEditor = false;
        NotifyStateChanged();
    }

    public string ConditionPreview() => BuildConditionExpression();

    public bool ConditionNeedsValue => GateEditorOperator is not
        ("exists" or "notexists" or "empty" or "notempty");

    private string BuildConditionExpression()
    {
        var path = string.IsNullOrWhiteSpace(GateEditorPath) ? "data" : GateEditorPath.Trim().TrimStart('$', '.');
        return ConditionNeedsValue
            ? $"{GateEditorOperator}:{path}:{GateEditorValue.Trim()}"
            : $"{GateEditorOperator}:{path}";
    }

    private void ParseCondition(string expression)
    {
        var parts = expression.Split(':', 3);
        GateEditorOperator = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : "exists";
        GateEditorPath = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : "data";
        GateEditorValue = parts.Length > 2 ? parts[2] : "";
    }

    public void RemoveGate(int stageIndex)
    {
        EdStageGates.Remove(stageIndex);
        NotifyStateChanged();
    }

    public void CancelGateEditor()
    {
        ShowGateEditor = false;
        NotifyStateChanged();
    }

    /// <summary>Build the gates dictionary for the canvas (non-null entries only, keyed by stage index).</summary>
    public Dictionary<int, ChainGateView> BuildCanvasGates()
    {
        var result = new Dictionary<int, ChainGateView>();
        foreach (var (index, gate) in EdStageGates)
        {
            if (gate is not null)
            {
                var v = ToViewGate(gate);
                if (v is not null) result[index] = v;
            }
        }
        return result;
    }

    /// <summary>Build a view model for the canvas from current editor state.</summary>
    public List<JobChainStageView> BuildCanvasStageViews()
    {
        var views = new List<JobChainStageView>(EdStages.Count);
        foreach (var (stage, i) in EdStages.Select((s, i) => (s, i)))
        {
            var stepViews = stage.Select(jobId => new JobChainStepView(jobId, JobName(jobId))).ToList();
            var gate = EdStageGates.GetValueOrDefault(i);
            views.Add(new JobChainStageView(stepViews, ToViewGate(gate)));
        }
        return views;
    }

    // ── Save ──────────────────────────────────────────────────────────────────────────────────

    public async Task SaveChainAsync()
    {
        EditorError = null;
        if (string.IsNullOrWhiteSpace(EdName)) { EditorError = "Name is required."; NotifyStateChanged(); return; }

        var populatedStages = EdStages
            .Select((stage, index) => (Stage: stage, OriginalIndex: index))
            .Where(item => item.Stage.Count > 0)
            .ToList();
        var stages = populatedStages.Select(item => (IReadOnlyList<Guid>)item.Stage.ToList()).ToList();
        if (stages.Count == 0) { EditorError = "Add at least one step."; NotifyStateChanged(); return; }
        var flatJobIds = populatedStages.SelectMany(item => item.Stage).ToList();

        // Build the parallel gates list — one per stage, null when no gate is set.
        IReadOnlyList<ChainGate?>? stageGates = null;
        if (EdStageGates.Count > 0)
            stageGates = populatedStages
                .Select(item => EdStageGates.GetValueOrDefault(item.OriginalIndex))
                .ToList();

        Saving = true;
        try
        {
            if (EditChainId.HasValue)
                await _svc.UpdateJobChainAsync(EditChainId.Value, EdName.Trim(),
                    string.IsNullOrWhiteSpace(EdDescription) ? null : EdDescription.Trim(), flatJobIds, stages, stageGates);
            else
                await _svc.CreateJobChainAsync(ProjectId, EdName.Trim(),
                    string.IsNullOrWhiteSpace(EdDescription) ? null : EdDescription.Trim(), flatJobIds, stages, stageGates);

            Chains = await _svc.ListJobChainsAsync(ProjectId);
            ShowEditor = false;
            Message = EditChainId.HasValue ? $"Chain '{EdName.Trim()}' updated." : $"Chain '{EdName.Trim()}' created.";
        }
        catch (Exception ex) { EditorError = ex.Message; }
        finally { Saving = false; NotifyStateChanged(); }
    }

    public async Task DeleteChainAsync(Guid chainId)
    {
        try
        {
            await _svc.DeleteJobChainAsync(chainId);
            Chains = await _svc.ListJobChainsAsync(ProjectId);
            ConfirmDeleteId = null;
            if (EditChainId == chainId) CloseEditor();
            Message = "Chain deleted.";
        }
        catch (Exception ex) { Message = ex.Message; }
        NotifyStateChanged();
    }

    // ── Gate view ↔ domain conversion ────────────────────────────────────────────────────────
    internal static ChainGateView? ToViewGate(ChainGate? gate) => gate switch
    {
        null => null,
        NoGate => null,
        WaitGate w => new WaitGateView(w.Duration.TotalSeconds),
        ConditionGate c => new ConditionGateView(c.Expression),
        _ => null,
    };

    private static ChainGate? FromViewGate(ChainGateView? gate) => gate switch
    {
        null => null,
        NoGateView => null,
        WaitGateView w => new WaitGate(TimeSpan.FromSeconds(w.DurationSeconds)),
        ConditionGateView c => new ConditionGate(c.Expression),
        _ => null,
    };
}
