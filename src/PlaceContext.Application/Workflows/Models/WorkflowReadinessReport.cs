namespace PlaceContext.Application.Workflows;

public sealed record WorkflowReadinessReport(
    int UniqueJobCount, int StageCount, IReadOnlyList<WorkflowReadinessFinding> Findings)
{
    public bool Ready => Findings.All(f => f.Severity != "error");
    public int ErrorCount => Findings.Count(f => f.Severity == "error");
    public int WarningCount => Findings.Count(f => f.Severity == "warning");
}
