namespace PlaceContext.Identity.OAuth;

/// <summary>Identity-local wire request for updating an MCP OAuth connection status.</summary>
public sealed record UpdateMcpOAuthStatusRequest(string Status);
