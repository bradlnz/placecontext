namespace PlaceContext.Agents.Infrastructure.Persistence;

public sealed class AgentAssignmentRow : IAgentsTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid StaffMemberId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? ParentAssignmentId { get; set; }
    public Guid? DelegatedByStaffMemberId { get; set; }
    public Guid? ScheduleId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Objective { get; set; } = string.Empty;
    public int ProfileVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ScheduledFor { get; set; }
    public string PlanSummary { get; set; } = string.Empty;
    public string ResultSummary { get; set; } = string.Empty;
    public string FailureSummary { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
