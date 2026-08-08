namespace PlaceContext.Agents.Contracts.Dtos;

public sealed record AgentAssignmentView(
    Guid Id, Guid StaffMemberId, Guid ProjectId, Guid? ParentAssignmentId,
    Guid? DelegatedByStaffMemberId, Guid? ScheduleId, Guid CreatedByUserId,
    string Objective, int ProfileVersion, string Status, DateTimeOffset? ScheduledFor,
    string PlanSummary, string ResultSummary, string FailureSummary,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
