namespace PlaceContext.Application.Ports;

/// <summary>One message in the conversation memory.</summary>
public sealed record AgentSessionMessage(
    string Role,
    string Content,
    DateTimeOffset Timestamp,
    List<AgentSessionToolCall>? ToolCalls = null);
