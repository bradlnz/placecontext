namespace PlaceContext.Application.Dtos;

/// <summary>One stage of a chain: the job(s) that run at this point. A stage with more than one job
/// is a parallel fan-out group; the stage right after it is the join.</summary>
public sealed record JobChainStageView(
    IReadOnlyList<JobChainStepView> Jobs,
    ChainGateView? Gate = null,
    IReadOnlyList<JobChainStageView>? ElseBranch = null)
{
    /// <summary>True when this stage fans out to more than one job.</summary>
    public bool IsParallel => Jobs.Count > 1;
}

/// <summary>Read model for a flow-control gate on a chain stage.</summary>
public abstract record ChainGateView;

/// <summary>No gate — the stage runs unconditionally.</summary>
public sealed record NoGateView : ChainGateView;

/// <summary>Pauses the pipeline before the stage.</summary>
public sealed record WaitGateView(double DurationSeconds) : ChainGateView;

/// <summary>Conditional routing gate.</summary>
public sealed record ConditionGateView(string Expression, IReadOnlyList<JobChainStageView>? ElseBranch = null) : ChainGateView;
