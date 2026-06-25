using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

/// <summary>
/// Test double for <see cref="IWorkloadRunner"/>. Returns canned results configured before the test
/// runs. Records every request received so tests can assert correct sharding, fan-out, and reduce
/// invocations.
/// </summary>
public sealed class FakeWorkloadRunner : IWorkloadRunner
{
    private readonly Queue<WorkloadRunResult> _canned = new();

    /// <summary>Every <see cref="WorkloadRunRequest"/> received, in order.</summary>
    public List<WorkloadRunRequest> ReceivedRequests { get; } = new();

    /// <summary>
    /// Enqueues a canned result returned by the next <see cref="RunAsync"/> call.
    /// If the queue is exhausted the fake returns a default success result (exit 0, minimal artifact).
    /// </summary>
    public void EnqueueResult(WorkloadRunResult result) => _canned.Enqueue(result);

    /// <summary>Convenience: enqueue N default success results.</summary>
    public void EnqueueSuccessResults(int count, string artifact = "{}")
    {
        for (var i = 0; i < count; i++)
            _canned.Enqueue(new WorkloadRunResult(0, artifact, "stdout", ""));
    }

    /// <summary>Convenience: enqueue a failed result (exit 1, no artifact).</summary>
    public void EnqueueFailedResult()
        => _canned.Enqueue(new WorkloadRunResult(1, null, "", "error"));

    /// <summary>Convenience: enqueue a partial result with a configurable exit code.</summary>
    public void EnqueuePartialResult(int exitCode, string artifact = @"{""partial"":true}")
        => _canned.Enqueue(new WorkloadRunResult(exitCode, artifact, "partial stdout", ""));

    public Task<WorkloadRunResult> RunAsync(WorkloadRunRequest request, CancellationToken ct = default)
    {
        ReceivedRequests.Add(request);

        if (_canned.TryDequeue(out var result))
            return Task.FromResult(result);

        // Default: success with minimal artifact.
        return Task.FromResult(new WorkloadRunResult(0, "{}", "stdout", ""));
    }
}
