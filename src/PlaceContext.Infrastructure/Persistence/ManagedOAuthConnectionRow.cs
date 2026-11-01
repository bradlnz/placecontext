namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Project-scoped, encrypted delegated OAuth credentials. Never sent to browsers.</summary>
public sealed class ManagedOAuthConnectionRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Provider { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string Scopes { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
