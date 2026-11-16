namespace PlaceContext.Infrastructure.Persistence;

public sealed class ExecutionApprovalRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RunId { get; set; }
    public string Warning { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? DecidedBy { get; set; }
}
