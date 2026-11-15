using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public bool ShowScoringEditor { get; private set; }
    public string ScoreInputField { get; set; } = "review";
    public string ScoreOutputField { get; set; } = "quality_gate";
    public double ScorePass { get; set; } = 90;
    public double ScoreConfidence { get; set; } = 0.8;
    public bool ScoreStopOnFailure { get; set; } = true;
    public string? ScoreError { get; private set; }
    public List<ScoreCriterionEditor> ScoreCriteria { get; } = [];
    private int _scoreStageIndex;
    private bool _editingScore;
    public IEnumerable<string> ScoreEvidenceFields => EdStageActions.Where(p => p.Key < _scoreStageIndex).Select(p => p.Value switch
    {
        LoopChainAction loop => loop.ResponseField,
        ContainerBuildChainAction build => build.OutputField,
        ContainerRunChainAction container => container.OutputField,
        VirtualMachineChainAction vm => vm.OutputField,
        QueryDataChainAction query => query.OutputField,
        ScoringGateChainAction score => score.OutputField,
        _ => null
    }).OfType<string>().Distinct();

    public void OpenScoringAction(int stageIndex, bool editing = false)
    {
        _scoreStageIndex = Math.Clamp(stageIndex, 0, EdStages.Count); _editingScore = editing;
        var action = editing ? EdStageActions.GetValueOrDefault(stageIndex) as ScoringGateChainAction : null;
        ScoreInputField = action?.InputField ?? ScoreEvidenceFields.LastOrDefault() ?? "review";
        ScoreOutputField = action?.OutputField ?? "quality_gate"; ScorePass = action?.PassScore ?? 90;
        ScoreConfidence = action?.MinimumConfidence ?? 0.8; ScoreStopOnFailure = action?.StopOnFailure ?? true;
        ScoreCriteria.Clear();
        if (action is not null) ScoreCriteria.AddRange(action.Criteria.Select(c => new ScoreCriterionEditor
            { Id = c.Id, Question = c.Question, Levels = string.Join('\n', c.Levels), Weight = c.Weight, MinimumScore = c.MinimumScore }));
        else ScoreCriteria.Add(new());
        ScoreError = null; ShowScoringEditor = true; NotifyStateChanged();
    }
    public void AddScoreCriterion()
    {
        if (ScoreCriteria.Count < 12)
        {
            var suffix = 1;
            while (ScoreCriteria.Any(c => c.Id == $"quality_{suffix}")) suffix++;
            ScoreCriteria.Add(new() { Id = $"quality_{suffix}" });
        }
        NotifyStateChanged();
    }
    public void RemoveScoreCriterion(ScoreCriterionEditor criterion) { ScoreCriteria.Remove(criterion); NotifyStateChanged(); }
    public void CloseScoringEditor() { ShowScoringEditor = false; NotifyStateChanged(); }
    public void SaveScoringAction()
    {
        try
        {
            var action = new ScoringGateChainAction(ScoreInputField, ScoreOutputField,
                ScoreCriteria.Select(c => new ScoreCriterion(c.Id, c.Question, c.Levels.Split('\n').Select(s => s.Trim()).ToArray(), c.Weight, c.MinimumScore)).ToArray(),
                ScorePass, ScoreConfidence, ScoreStopOnFailure);
            if (!_editingScore)
            {
                ShiftGatesForInsert(_scoreStageIndex); ShiftActionsForInsert(_scoreStageIndex); EdStages.Insert(_scoreStageIndex, []);
            }
            EdStageActions[_scoreStageIndex] = action; ShowScoringEditor = false; ScoreError = null;
        }
        catch (ArgumentException ex) { ScoreError = ex.Message; }
        NotifyStateChanged();
    }
    public sealed class ScoreCriterionEditor
    {
        public string Id { get; set; } = "quality";
        public string Question { get; set; } = "Does the evidence demonstrate the acceptance criteria are met?";
        public string Levels { get; set; } = "Evidence is missing or acceptance criteria fail.\nSome acceptance criteria have evidence; gaps remain.\nAll acceptance criteria have current, specific passing evidence.";
        public double Weight { get; set; } = 1;
        public double MinimumScore { get; set; } = 80;
    }
}
