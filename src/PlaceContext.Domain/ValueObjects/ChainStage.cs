namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// One stage of a <see cref="Entities.JobChain"/> pipeline: the job(s) that run at this point.
/// A stage with a single job is an ordinary sequential step (this is what every stage was before
/// fan-out/fan-in existed — a chain of all size-1 stages is exactly the old linear chain). A stage
/// with more than one job is a parallel fan-out group; the stage immediately after it is the join —
/// it runs once every branch in the fan-out group has reached a terminal state and receives all of
/// their outputs. The same job id may repeat within or across stages — no dedup.
/// </summary>
public sealed class ChainStage
{
    private readonly List<Guid> _jobIds;

    public ChainStage(IEnumerable<Guid> jobIds)
    {
        if (jobIds is null) throw new ArgumentNullException(nameof(jobIds));
        _jobIds = jobIds.ToList();
        if (_jobIds.Count == 0)
            throw new ArgumentException("A chain stage needs at least one job.", nameof(jobIds));
        if (_jobIds.Any(id => id == Guid.Empty))
            throw new ArgumentException("Chain stage jobs must be real job ids.", nameof(jobIds));
    }

    /// <summary>The job(s) that run at this stage — run in parallel when there is more than one.</summary>
    public IReadOnlyList<Guid> JobIds => _jobIds;

    /// <summary>True when this stage fans out to more than one job.</summary>
    public bool IsParallel => _jobIds.Count > 1;

    /// <summary>A single-job (ordinary sequential) stage.</summary>
    public static ChainStage Of(Guid jobId) => new(new[] { jobId });
}
