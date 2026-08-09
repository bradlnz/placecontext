namespace PlaceContext.Application.Ports;

/// <summary>A tool call within a message.</summary>
public sealed record AgentSessionToolCall(
    string ToolName,
    string Args,
    string Status,
    string? Result = null,
    string ResultType = "text");
