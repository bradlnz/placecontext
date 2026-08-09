namespace PlaceContext.Communications.Infrastructure.Persistence;

public sealed class CommunicationProviderRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Channel { get; set; } = "email";
    public string Kind { get; set; } = "postmark";
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool IsDefault { get; set; }
    public bool UseForTwoFactor { get; set; }
    public string AuthType { get; set; } = "none";
    public string? AuthHeaderName { get; set; }
    public Guid? VaultProjectId { get; set; }
    public string? ApiKeySecretName { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
