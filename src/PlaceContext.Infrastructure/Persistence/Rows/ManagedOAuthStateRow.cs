namespace PlaceContext.Infrastructure.Persistence;

/// <summary>One-time OAuth handshake, keyed by SHA-256 state digest rather than plaintext state.</summary>
public sealed class ManagedOAuthStateRow : ITenantOwned
{
    public Guid Id
    {
        get; set;
    }
    public Guid TenantId
    {
        get; set;
    }
    public Guid ProjectId
    {
        get; set;
    }
    public Guid UserId
    {
        get; set;
    }
    public string StateHash { get; set; } = "";
    public string PkceVerifier { get; set; } = "";
    public string Provider { get; set; } = "";
    public DateTimeOffset ExpiresAt
    {
        get; set;
    }
}
