namespace PlaceContext.Domain.Mcp;

/// <summary>
/// Wire values for MCP connection transport and auth. Match <see cref="Entities.McpConnection"/> field comments
/// and every compare/assign site — never hardcode these strings.
/// </summary>
public static class McpTransport
{
    public const string Http = "http";
    public const string Sse = "sse";
    public const string Stdio = "stdio";
}
