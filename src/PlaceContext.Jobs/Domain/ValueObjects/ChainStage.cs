namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// One stage of a <see cref="Entities.JobChain"/> pipeline: the job(s) that run at this point.
/// A stage with a single job is an ordinary sequential step (this is what every stage was before
/// fan-out/fan-in existed — a chain of all size-1 stages is exactly the old linear chain). A stage
/// with more than one job is a parallel fan-out group; the stage immediately after it is the join —
/// it runs once every branch in the fan-out group has reached a terminal state and receives all of
/// their outputs. The same job id may repeat within or across stages — no dedup.
///
/// Optionally carries a <see cref="Gate"/> to add flow-control: a wait pause before the stage, or
/// a condition expression that routes the pipeline. When the gate's <see cref="GateResult.Proceed"/>
/// is false, <see cref="ElseBranch"/> stages (if any) run instead; otherwise the stage is skipped
/// and the pipeline continues with the next sibling stage.
/// </summary>
public sealed class ChainStage
{
    private readonly List<Guid> _jobIds;
    private readonly List<ChainStage>? _elseBranch;

    public ChainStage(IEnumerable<Guid> jobIds, ChainGate? gate = null,
        IReadOnlyList<ChainStage>? elseBranch = null, ChainAction? action = null)
    {
        if (jobIds is null) throw new ArgumentNullException(nameof(jobIds));
        _jobIds = jobIds.ToList();
        if (_jobIds.Count == 0 && action is null)
            throw new ArgumentException("A chain stage needs at least one job or an action.", nameof(jobIds));
        if (_jobIds.Count > 0 && action is not null)
            throw new ArgumentException("A chain stage cannot mix jobs and an action.", nameof(action));
        if (_jobIds.Any(id => id == Guid.Empty))
            throw new ArgumentException("Chain stage jobs must be real job ids.", nameof(jobIds));
        _elseBranch = elseBranch?.ToList();
        Gate = gate;
        Action = action;
    }

    /// <summary>The job(s) that run at this stage — run in parallel when there is more than one.</summary>
    public IReadOnlyList<Guid> JobIds => _jobIds;

    /// <summary>True when this stage fans out to more than one job.</summary>
    public bool IsParallel => _jobIds.Count > 1;

    /// <summary>A typed non-job action for this stage, mutually exclusive with <see cref="JobIds"/>.</summary>
    public ChainAction? Action { get; }

    /// <summary>Number of run-history entries this stage creates.</summary>
    public int ExecutionCount => Action is null ? _jobIds.Count : 1;

    /// <summary>Optional flow-control gate executed before this stage's jobs.</summary>
    public ChainGate? Gate { get; }

    /// <summary>
    /// Optional alternative stages that run instead of this stage when a <see cref="ConditionGate"/>
    /// evaluates to false. When null and the gate's condition is false, the stage is simply skipped
    /// and the pipeline continues with the next sibling stage.
    /// </summary>
    public IReadOnlyList<ChainStage>? ElseBranch => _elseBranch;

    /// <summary>A single-job (ordinary sequential) stage with no gate.</summary>
    public static ChainStage Of(Guid jobId) => new(new[] { jobId });

    /// <summary>A stage containing one typed action.</summary>
    public static ChainStage ForAction(ChainAction action)
        => new(Array.Empty<Guid>(), action: action ?? throw new ArgumentNullException(nameof(action)));
}
