namespace PlaceContext.Domain.ValueObjects;

public sealed class ScoringGateChainAction : ChainAction
{
    public const string ActionType = "scoringGate";
    public ScoringGateChainAction(string inputField, string outputField, IReadOnlyList<ScoreCriterion> criteria,
        double passScore = 90, double minimumConfidence = 0.8, bool stopOnFailure = true)
    {
        if (string.IsNullOrWhiteSpace(inputField) || inputField.Length > 200 || inputField.Split('.').Any(p => !ValidField(p)))
            throw new ArgumentException("Choose an evidence field or dotted field path.");
        if (!ValidField(outputField)) throw new ArgumentException("Output field must be a name of up to 80 letters, digits or underscores.");
        if (inputField.Split('.')[0] == outputField) throw new ArgumentException("Evidence and output fields must be different.");
        if (criteria is null || criteria.Count is < 1 or > 12 || criteria.Any(c => c is null)
            || criteria.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != criteria.Count)
            throw new ArgumentException("Add 1–12 criteria with unique field names.");
        if (!double.IsFinite(passScore) || passScore is < 0 or > 100
            || !double.IsFinite(minimumConfidence) || minimumConfidence is < 0 or > 1)
            throw new ArgumentException("Pass score must be 0–100 and confidence 0–1.");
        InputField = inputField; OutputField = outputField; Criteria = criteria.ToArray();
        PassScore = passScore; MinimumConfidence = minimumConfidence; StopOnFailure = stopOnFailure;
    }
    internal static bool ValidField(string? value) => value is { Length: > 0 and <= 80 }
        && (char.IsAsciiLetter(value[0]) || value[0] == '_') && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    public override string Type => ActionType;
    public override string DisplayName => "Scoring gate";
    public string InputField { get; }
    public string OutputField { get; }
    public IReadOnlyList<ScoreCriterion> Criteria { get; }
    public double PassScore { get; }
    public double MinimumConfidence { get; }
    public bool StopOnFailure { get; }
}

public sealed class ScoreCriterion
{
    public ScoreCriterion(string id, string question, IReadOnlyList<string> levels, double weight = 1, double minimumScore = 0)
    {
        if (!ScoringGateChainAction.ValidField(id)) throw new ArgumentException("Each criterion needs a field name.");
        if (string.IsNullOrWhiteSpace(question) || question.Length > 4000) throw new ArgumentException("Each criterion needs a question of up to 4,000 characters.");
        if (levels is null || levels.Count is < 2 or > 10 || levels.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 1000))
            throw new ArgumentException("Each criterion needs 2–10 descriptive levels, lowest to highest (up to 1,000 characters each).");
        if (!double.IsFinite(weight) || weight is <= 0 or > 100 || !double.IsFinite(minimumScore) || minimumScore is < 0 or > 100)
            throw new ArgumentException("Weights must be greater than 0 and up to 100; minimum scores must be 0–100.");
        Id = id; Question = question.Trim(); Levels = levels.ToArray(); Weight = weight; MinimumScore = minimumScore;
    }
    public string Id { get; }
    public string Question { get; }
    public IReadOnlyList<string> Levels { get; }
    public double Weight { get; }
    public double MinimumScore { get; }
}
