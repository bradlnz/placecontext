namespace PlaceContext.Application.Ports;

/// <summary>
/// Durable hand-off from trigger firing (cron scheduler + event fan-out) to the background runner that
/// actually executes the job. The production adapter is DB-backed, so enqueue participates in the
/// firing transaction and any replica can drain it (atomic claiming) — correct across k3s replicas and
/// surviving restarts. "Enqueue a run" semantics: each enqueue produces an independent run; concurrent
/// runs of the same job are allowed.
/// </summary>
public interface IJobRunQueue
{
    /// <summary>Queues a job run for background execution (within the caller's unit of work).</summary>
    Task EnqueueAsync(QueuedJobRun run, CancellationToken ct = default);
}
