namespace PlaceContext.Infrastructure.Persistence;

/// <summary>One Postmark configuration per tenant. The actual API token stays in project Vault.</summary>
public sealed class PostmarkConnectionRow : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid VaultProjectId { get; set; }
    public string ServerTokenSecretName { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "PlaceContext";
    public string MessageStream { get; set; } = "outbound";
    public DateTimeOffset ConfiguredAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
