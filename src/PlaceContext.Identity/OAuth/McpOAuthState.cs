namespace PlaceContext.Identity.OAuth;

internal sealed class McpOAuthState
{
    public Guid ConnectionId { get; set; }
    public string CodeVerifier { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string TokenEndpoint { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string TenantSlug { get; set; } = string.Empty;
    public string TenantTimeZone { get; set; } = "UTC";
    public Guid UserId { get; set; }
}
