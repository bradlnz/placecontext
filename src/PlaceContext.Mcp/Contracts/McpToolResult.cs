using System.Text.Json;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Mcp;

public sealed record McpToolResult(
    bool Success,
    string? Content,
    string? Error,
    JsonElement? RawContent = null);
