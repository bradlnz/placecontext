namespace PlaceContext.Agents.Infrastructure.Persistence;

public sealed class AgentApprovalRow : IAgentsTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AssignmentId { get; set; }
    public string ActionKind { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = string.Empty;
    public Guid? ResolvedByUserId { get; set; }
    public string ReviewerComment { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
