using System.Net.Http.Json;
using System.Text.Json;

namespace PlaceContext.Application.Mcp;

public sealed record McpConnectionInfo(
    Guid Id,
    string Name,
    string Transport,
    string? EndpointUrl,
    bool Enabled);
