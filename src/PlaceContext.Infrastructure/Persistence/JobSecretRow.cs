namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Flat EF row for one encrypted vault secret. <see cref="Cipher"/> is the protected value
/// (AES via Data Protection) — plaintext is never stored.</summary>
public sealed class JobSecretRow : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string Cipher { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
