using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Mcp;

public interface IMcpConnectionProbe
{
    Task<string> ProbeAsync(McpConnection connection, CancellationToken ct = default);
}
