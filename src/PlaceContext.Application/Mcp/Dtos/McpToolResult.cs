using System.Text.Json;

namespace PlaceContext.Application.Mcp;

public sealed record McpToolResult(
    bool Success,
    string? Content,
    string? Error,
    JsonElement? RawContent = null);
