namespace PlaceContext.Infrastructure.Persistence;

public sealed class WorkflowDemoShareRow : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid RunId { get; set; }
    public Guid ProjectId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public string Content { get; set; } = "";
}
