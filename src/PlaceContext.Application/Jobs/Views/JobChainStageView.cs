namespace PlaceContext.Application.Dtos;

/// <summary>One stage of a chain: the job(s) that run at this point. A stage with more than one job
/// is a parallel fan-out group; the stage right after it is the join.</summary>
public sealed record JobChainStageView(IReadOnlyList<JobChainStepView> Jobs)
{
    /// <summary>True when this stage fans out to more than one job.</summary>
    public bool IsParallel => Jobs.Count > 1;
}
