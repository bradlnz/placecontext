namespace PlaceContext.Infrastructure.Connections;

public sealed class ManagedOfficeOAuthOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    /// <summary>Exact callback registered as a Web redirect URI in Microsoft Entra ID.</summary>
    public string RedirectUri { get; set; } = "";
    /// <summary>Use a tenant-specific Entra authority if a single-tenant app is desired.</summary>
    public string Authority { get; set; } = "organizations";
}
