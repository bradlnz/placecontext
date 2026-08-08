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
