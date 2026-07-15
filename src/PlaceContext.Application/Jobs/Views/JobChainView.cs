namespace PlaceContext.Application.Dtos;

/// <summary>Read model for a job chain definition, as its ordered stages.</summary>
public sealed record JobChainView(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    IReadOnlyList<JobChainStageView> Stages,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// Backward-compatible flattened view of every job across every stage, in run order. For a
    /// linear chain (every stage size 1) this is exactly the original step list.
    /// </summary>
    public IReadOnlyList<JobChainStepView> Steps => Stages.SelectMany(s => s.Jobs).ToList();
}
