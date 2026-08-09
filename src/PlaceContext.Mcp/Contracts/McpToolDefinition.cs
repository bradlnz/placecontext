using System.Text.Json;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Mcp;

public sealed record McpToolDefinition(
    string Name,
    string? Description,
    JsonElement? InputSchema);
