using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public Dictionary<int, StepExecutionPolicy?> EdStagePolicies { get; } = new();
    public int? PolicyStageIndex { get; private set; }
    public bool PolicyIsFalsePath { get; private set; }
    public int StepMaxRetries { get; set; }
    public int StepRetryDelaySeconds { get; set; } = 5;
    public int? StepTimeoutSeconds { get; set; }
    public string? StepPolicyError { get; private set; }

    public void OpenStepPolicy(int index) => OpenStepPolicy(index, false);
    public void OpenElseStepPolicy(int index) => OpenStepPolicy(index, true);
    private void OpenStepPolicy(int index, bool falsePath)
    {
        if (index < 0 || index >= (falsePath ? GateEditorElseStages.Count : EdStages.Count)) return;
        PolicyIsFalsePath = falsePath;
        var policy = falsePath ? GateEditorElseStages[index].ExecutionPolicy : EdStagePolicies.GetValueOrDefault(index);
        PolicyStageIndex = index;
        StepMaxRetries = policy?.MaxRetries ?? 0;
        StepRetryDelaySeconds = policy?.RetryDelaySeconds ?? 5;
        StepTimeoutSeconds = policy?.TimeoutSeconds;
        StepPolicyError = null;
        NotifyStateChanged();
    }

    public void CloseStepPolicy() { PolicyStageIndex = null; NotifyStateChanged(); }
    public void SaveStepPolicy()
    {
        if (PolicyStageIndex is not { } index) return;
        try
        {
            SetStepPolicy(index, new StepExecutionPolicy(StepMaxRetries, StepRetryDelaySeconds, StepTimeoutSeconds));
            CloseStepPolicy();
        }
        catch (ArgumentException ex) { StepPolicyError = ex.Message; NotifyStateChanged(); }
    }
    public void ResetStepPolicy()
    {
        if (PolicyStageIndex is { } index) SetStepPolicy(index, null);
        CloseStepPolicy();
    }

    private void SetStepPolicy(int index, StepExecutionPolicy? policy)
    {
        if (!PolicyIsFalsePath) { EdStagePolicies[index] = policy; return; }
        var stage = GateEditorElseStages[index];
        GateEditorElseStages[index] = new(stage.JobIds, stage.Gate, stage.ElseBranch, stage.Action, policy);
    }

    private void ShiftPolicies(int index, bool inserting)
    {
        var shifted = EdStagePolicies.Where(pair => inserting || pair.Key != index)
            .ToDictionary(pair => pair.Key >= index ? pair.Key + (inserting ? 1 : -1) : pair.Key, pair => pair.Value);
        EdStagePolicies.Clear();
        foreach (var (key, value) in shifted) EdStagePolicies[key] = value;
    }
}
