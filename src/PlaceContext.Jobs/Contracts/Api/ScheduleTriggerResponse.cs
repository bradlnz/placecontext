namespace PlaceContext.Jobs.Contracts.Api;

public sealed record ScheduleTriggerResponse(
    Guid Id,
    string Name,
    string Kind,
    bool Enabled,
    string? CronExpression,
    string? EventName,
    Guid? JobId,
    Guid? ChainId,
    string? SourceTable,
    string? Prompt,
    string TargetLabel,
    string NextRunLabel,
    string LastFiredLabel);
