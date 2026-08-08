namespace PlaceContext.AgentChat.Infrastructure.Caching;

/// <summary>A tool call within a message.</summary>
public sealed record ChatMemoryToolCall(
    string ToolName,
    string Args,
    string Status,
    string? Result = null,
    string ResultType = "text");
