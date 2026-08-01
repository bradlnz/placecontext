namespace PlaceContext.Infrastructure.Persistence;

/// <summary>
/// One configured communication provider (email or SMS) per tenant. Only a *reference* to a
/// project Vault secret is stored — the API key/token itself is resolved and decrypted at send
/// time, so Vault rotation takes effect without reconfiguring the provider.
/// </summary>
public sealed class CommunicationProviderRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>"email" | "sms"</summary>
    public string Channel { get; set; } = "email";
    /// <summary>Payload shape: "postmark" | "sendgrid" | "twilio".</summary>
    public string Kind { get; set; } = "postmark";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>At most one per channel — the provider used for regular sends.</summary>
    public bool IsDefault { get; set; }
    /// <summary>At most one per channel — the provider used for authentication codes.</summary>
    public bool UseForTwoFactor { get; set; }
    /// <summary>"none" | "bearer" | "header" | "basic"</summary>
    public string AuthType { get; set; } = "none";
    public string? AuthHeaderName { get; set; }
    public Guid? VaultProjectId { get; set; }
    public string? ApiKeySecretName { get; set; }
    /// <summary>Non-secret per-kind fields (sender identity, endpoint overrides, …) as JSON.</summary>
    public string SettingsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
