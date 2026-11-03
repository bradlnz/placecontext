namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Global, short-lived email challenge and one-use tenant-scoped sign-in ticket.
/// Never contains plaintext passwords or authentication tokens.</summary>
public sealed class PendingWorkspaceSignupRow
{
    public Guid Id
    {
        get; set;
    }
    public string Slug { get; set; } = "";
    public string WorkspaceName { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Plan { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt
    {
        get; set;
    }
    public DateTimeOffset ExpiresAt
    {
        get; set;
    }
    public DateTimeOffset? VerifiedAt
    {
        get; set;
    }
    public Guid? TenantId
    {
        get; set;
    }
    public Guid? UserId
    {
        get; set;
    }
    public string? ExchangeHash
    {
        get; set;
    }
    public DateTimeOffset? ExchangeExpiresAt
    {
        get; set;
    }
    public DateTimeOffset? ExchangeRedeemedAt
    {
        get; set;
    }
}
