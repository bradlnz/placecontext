namespace PlaceContext.Jobs.Contracts.Api;

public sealed record CreateSchedulePageTriggerRequest(
    string Name,
    string Kind,
    Guid? JobId,
    Guid? ChainId,
    string? CronExpression,
    string? EventName,
    string? SourceTable,
    string? Prompt);
