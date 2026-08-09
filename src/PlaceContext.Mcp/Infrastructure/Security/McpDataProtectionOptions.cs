namespace PlaceContext.Mcp.Infrastructure.Security;

public sealed class McpDataProtectionOptions
{
    public const string SectionName = "PlaceContext:Mcp:DataProtection";

    /// <summary>Shared durable key-ring directory used to decrypt existing MCP OAuth tokens.</summary>
    public string? KeyDirectory { get; set; }
}
