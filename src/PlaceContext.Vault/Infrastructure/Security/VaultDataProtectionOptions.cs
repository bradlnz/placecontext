namespace PlaceContext.Vault.Infrastructure.Security;

public sealed class VaultDataProtectionOptions
{
    public const string SectionName = "PlaceContext:Vault:DataProtection";

    /// <summary>
    /// Shared key-ring directory for Vault replicas. Production deployments should mount this from
    /// durable encrypted storage or replace Data Protection with a managed key provider.
    /// </summary>
    public string? KeyDirectory { get; set; }
}
