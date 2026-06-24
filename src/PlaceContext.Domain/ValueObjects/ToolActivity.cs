namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// One aggregated MCP tool invocation, as the assembler consumes it. Distilled from the persisted
/// tool-call log so the Domain never sees the Application's logging types.
/// </summary>
public readonly record struct ToolActivity(string Tool, bool Failed);
