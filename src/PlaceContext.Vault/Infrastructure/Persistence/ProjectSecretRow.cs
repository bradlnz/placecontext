namespace PlaceContext.Vault.Infrastructure.Persistence;

/// <summary>Encrypted Vault secret persisted for one tenant project.</summary>
public sealed class ProjectSecretRow
{
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Cipher { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
