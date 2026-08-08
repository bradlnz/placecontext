namespace PlaceContext.Application.Dtos;

public sealed record TriggerView(
    Guid Id,
    Guid ProjectId,
    /// <summary>Null for launchpads (they target a chain, not a job).</summary>
    Guid? JobId,
    string Name,
    /// <summary>"Schedule" | "Event" | "Launchpad" | "Command".</summary>
    string Kind,
    bool Enabled,
    string? CronExpression,
    string? EventName,
    Guid? ChainId,
    string? SourceTable,
    string? Prompt,
    Guid? CommandId,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastFiredAt,
    DateTimeOffset CreatedAt);
