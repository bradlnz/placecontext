namespace PlaceContext.Operations.Contracts.Api;

public sealed record InspectorToolCallResponse(
    string Id,
    string Tool,
    string Direction,
    string Project,
    string Summary,
    string Status,
    long DurationMs,
    string RequestJson,
    string ResponseJson,
    DateTimeOffset At);
