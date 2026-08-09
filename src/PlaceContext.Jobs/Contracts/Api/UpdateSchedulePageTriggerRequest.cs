namespace PlaceContext.Jobs.Contracts.Api;

public sealed record UpdateSchedulePageTriggerRequest(
    string? Name,
    string? CronExpression,
    string? EventName,
    bool? Enabled);
