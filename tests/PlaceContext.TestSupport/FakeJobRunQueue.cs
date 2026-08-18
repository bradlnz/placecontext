using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

/// <summary>Records enqueued runs so tests can assert trigger fan-out without a background runner.</summary>
public sealed class FakeJobRunQueue : IJobRunQueue
{
    public List<QueuedJobRun> Enqueued { get; } = new();

    public Task EnqueueAsync(QueuedJobRun run, CancellationToken ct = default)
    {
        Enqueued.Add(run);
        return Task.CompletedTask;
    }
}
