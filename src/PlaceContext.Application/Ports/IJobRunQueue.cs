namespace PlaceContext.Application.Ports;

/// <summary>
/// A job run requested by a trigger, carrying the tenant it belongs to so the background consumer can
/// re-establish the ambient tenant before dispatching (triggers fire outside any HTTP request).
/// <paramref name="Payload"/> carries optional parameters injected by an event source (e.g. a form's
/// fields delivered via an external queue) to override the job's default inputs; null for plain
/// schedule ticks.
/// </summary>
public sealed record QueuedJobRun(
    Guid TenantId, Guid JobId, Guid TriggerId, string TriggerName, string? Payload = null);

/// <summary>
/// In-process hand-off from trigger firing (cron scheduler + event fan-out) to the background runner
/// that actually executes the job. Decouples the firing request from the (slow, container-backed) run
/// so emitting an event or ticking the scheduler never blocks on Docker. "Enqueue a run" semantics:
/// each enqueue produces an independent run; concurrent runs of the same job are allowed.
/// </summary>
public interface IJobRunQueue
{
    /// <summary>Queues a job run for background execution. Non-blocking.</summary>
    void Enqueue(QueuedJobRun run);
}
