namespace PlaceContext.Host.Controllers;

internal sealed class McpOAuthState
{
    public Guid ConnectionId { get; set; }
    public string CodeVerifier { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string TokenEndpoint { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
    public string? State { get; set; }
}
