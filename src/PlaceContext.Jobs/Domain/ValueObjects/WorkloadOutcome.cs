namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// The outcome of a single workload shard execution, derived by applying <see cref="ExitCodePolicy"/>
/// to the container's raw exit code.
/// </summary>
public enum WorkloadOutcome
{
    /// <summary>Exit code matched a success code in the job's <see cref="ExitCodePolicy"/>.</summary>
    Succeeded,
    /// <summary>Exit code matched a partial code in the job's <see cref="ExitCodePolicy"/>.</summary>
    Partial,
    /// <summary>Exit code did not match any success or partial code.</summary>
    Failed,
}
