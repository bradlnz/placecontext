using System.Text.Json;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Mcp;

public sealed record McpConnectionInfo(
    Guid Id,
    string Name,
    string Transport,
    string? EndpointUrl,
    bool Enabled);
