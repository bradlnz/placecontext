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

public static class McpAuthType
{
    public const string None = "none";
    public const string Bearer = "bearer";
    public const string Header = "header";
    public const string ApiKey = "apikey";
    public const string OAuth = "oauth";
}
