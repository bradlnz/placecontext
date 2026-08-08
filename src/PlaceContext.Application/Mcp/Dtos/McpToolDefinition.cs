using System.Net.Http.Json;
using System.Text.Json;

namespace PlaceContext.Application.Mcp;

public sealed record McpToolDefinition(
    string Name,
    string? Description,
    JsonElement? InputSchema);
