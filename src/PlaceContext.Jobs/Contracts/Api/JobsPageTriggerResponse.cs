namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobsPageTriggerResponse(
    Guid Id,
    Guid? JobId,
    string Name,
    string Kind,
    bool Enabled,
    string? CronExpression,
    string? EventName);
