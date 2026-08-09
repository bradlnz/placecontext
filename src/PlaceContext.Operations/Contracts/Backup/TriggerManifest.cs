using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Operations.Contracts.Backup;

/// <summary>A schedule/event trigger on a job, or a launchpad on a chain. Natural key on import is (JobId, Name).</summary>
public sealed record TriggerManifest(
    Guid TriggerId, Guid ProjectId,
    /// <summary>Null for launchpads (they target <see cref="ChainId"/>).</summary>
    Guid? JobId, string Name,
    /// <summary>"Schedule" | "Event" | "Launchpad".</summary>
    string Kind,
    bool Enabled, string? CronExpression, string? EventName,
    Guid? ChainId = null, string? SourceTable = null, string? Prompt = null);
