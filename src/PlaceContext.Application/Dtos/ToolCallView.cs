namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one row in the MCP Inspector tool-call feed.</summary>
public sealed record ToolCallView(
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
