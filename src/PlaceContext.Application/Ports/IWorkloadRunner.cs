namespace PlaceContext.Application.Ports;

/// <summary>
/// Port: runs a single generic container workload and returns the raw result.
/// Infrastructure implements this via Docker (or a compatible runtime).
/// PlaceContext has no knowledge of what the container does — all inputs and outputs are opaque.
/// </summary>
public interface IWorkloadRunner
{
    /// <summary>
    /// Runs one container, writes the provided <see cref="WorkloadRunRequest.StdinPayload"/> to STDIN,
    /// waits for it to exit, and returns the captured exit code, artifact, and stdout/stderr.
    /// Never throws for a clean container failure; only propagates infrastructure-level exceptions
    /// (e.g. docker not found, timeout from caller cancellation).
    /// </summary>
    Task<WorkloadRunResult> RunAsync(WorkloadRunRequest request, CancellationToken ct = default);

    /// <summary>
    /// Cancels a running workload identified by <paramref name="correlationId"/>.
    /// For Docker: kills the container. For Kubernetes: deletes the Job.
    /// Best-effort — does not throw if the workload no longer exists.
    /// </summary>
    Task CancelAsync(string correlationId, CancellationToken ct = default);
}
