using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// The outcome of a single shard (one map container invocation). Owned by its <see cref="JobRun"/>.
/// The artifact is an opaque JSON string — PlaceContext does not interpret its content.
/// </summary>
public sealed class ShardResult
{
    public ShardResult(
        int index,
        int exitCode,
        WorkloadOutcome outcome,
        string? artifact,
        string? log)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index), "Shard index must be >= 0.");

        Index = index;
        ExitCode = exitCode;
        Outcome = outcome;
        Artifact = artifact;
        Log = log;
    }

    /// <summary>Zero-based shard index (matches the position in <see cref="MapSpec.InputPayloads"/>).</summary>
    public int Index { get; }

    /// <summary>Raw exit code returned by the container.</summary>
    public int ExitCode { get; }

    /// <summary>Outcome derived from the exit code via the job's <see cref="ExitCodePolicy"/>.</summary>
    public WorkloadOutcome Outcome { get; }

    /// <summary>Opaque JSON artifact written by the container to /out/result.json. Null if not produced.</summary>
    public string? Artifact { get; }

    /// <summary>Combined stdout/stderr captured from the container. May be truncated.</summary>
    public string? Log { get; }
}
