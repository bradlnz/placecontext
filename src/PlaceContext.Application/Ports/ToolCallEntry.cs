namespace PlaceContext.Application.Ports;

/// <summary>One recorded MCP tool invocation, surfaced live on the portal's Inspector view.</summary>
public sealed record ToolCallEntry(
    string Id,
    string Tool,
    string Direction,
    string Project,
    string Summary,
    ToolCallStatus Status,
    long DurationMs,
    string RequestJson,
    string ResponseJson,
    DateTimeOffset At);
