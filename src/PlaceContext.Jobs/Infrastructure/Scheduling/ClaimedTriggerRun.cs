namespace PlaceContext.Jobs.Infrastructure.Scheduling;

/// <summary>A trigger run atomically claimed from the durable scheduling queue.</summary>
internal sealed record ClaimedTriggerRun(
    Guid Id,
    Guid TenantId,
    Guid JobId,
    Guid TriggerId,
    string TriggerName,
    string? Payload);
