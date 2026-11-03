namespace PlaceContext.Application.Dtos;

/// <summary>
/// A chain run as a staged pipeline: one entry per step with its live status (steps after a
/// failure show Skipped), plus the final payload — the last executed stage's primary output.
/// Persisted from the moment the run starts, so the portal can watch the stages progress and keep
/// a history. A stage with more than one step is a parallel fan-out group that ran concurrently.
/// </summary>
public sealed record ChainRunView(
    Guid Id,
    Guid ChainId,
    string ChainName,
    string Status,
    IReadOnlyList<ChainStepRunView> Steps,
    string? FinalOutput,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt)
{
    public Guid ProjectId
    {
        get; init;
    }
    public ChainInputRequest? InputRequest
    {
        get; init;
    }
    private IReadOnlyList<IReadOnlyList<ChainStepRunView>>? _stepsByStage;
    private IReadOnlyList<ChainStepRunView>? _groupedSteps;

    /// <summary>Steps grouped by stage, in run order — a group with more than one entry is a
    /// parallel fan-out stage (the join that follows one has a single entry, same as any other
    /// sequential step).</summary>
    public IReadOnlyList<IReadOnlyList<ChainStepRunView>> StepsByStage
    {
        get
        {
            if (_stepsByStage is null || !ReferenceEquals(_groupedSteps, Steps))
            {
                _stepsByStage = Steps
                    .GroupBy(s => s.StageIndex)
                    .OrderBy(g => g.Key)
                    .Select(g => (IReadOnlyList<ChainStepRunView>)g.OrderBy(s => s.BranchIndex).ToList())
                    .ToList();
                _groupedSteps = Steps;
            }
            return _stepsByStage;
        }
    }
}
