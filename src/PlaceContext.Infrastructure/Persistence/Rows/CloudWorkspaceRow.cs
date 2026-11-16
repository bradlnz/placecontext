namespace PlaceContext.Infrastructure.Persistence;

public sealed class CloudWorkspaceRow : ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Plan { get; set; } = "basic";
    public string State { get; set; } = "pending";
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public Guid? LeaseToken { get; set; }
}
