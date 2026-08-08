namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record UpdateSchedulePageTriggerRequest(
    string? Name,
    string? CronExpression,
    string? EventName,
    bool? Enabled);
