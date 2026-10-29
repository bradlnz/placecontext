using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

/// <summary>A schedule/event trigger on a job. Natural key on import is (JobId, Name).</summary>
public sealed record TriggerManifest(
    Guid TriggerId, Guid ProjectId,
    Guid? JobId, string Name,
    /// <summary>"Schedule" | "Event".</summary>
    string Kind,
    bool Enabled, string? CronExpression, string? EventName);
