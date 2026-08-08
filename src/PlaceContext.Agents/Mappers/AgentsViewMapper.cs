using PlaceContext.Agents.Contracts.Dtos;
using PlaceContext.Agents.Domain.Entities;

namespace PlaceContext.Agents.Mappers;

internal static class AgentsViewMapper
{
    public static AgentProfileView ToView(AgentProfile profile) => new(
        profile.Id, profile.Name, profile.Role, profile.Description, profile.Responsibilities,
        profile.SystemInstructions, profile.Provider, profile.Model, profile.ReasoningLevel,
        profile.AllowedTools, profile.AllowedJobIds, profile.AllowedJobChainIds,
        profile.Skills, profile.Permissions, profile.RequirePlanApproval,
        profile.RequireExternalActionApproval, profile.RequireJobDraftApproval,
        profile.MaxTokensPerAssignment, profile.MaxCostPerAssignment,
        profile.MaxExecutionMinutes, profile.MaxRetries, profile.MaxDelegationDepth,
        profile.ConcurrencyLimit, profile.Version, profile.CreatedAt, profile.UpdatedAt);

    public static StaffMemberView ToView(StaffMember staff) => new(
        staff.Id, staff.ProfileId, staff.Name, staff.ProjectIds,
        staff.InstructionsOverride, staff.ModelOverride, staff.Status.ToString(),
        staff.CreatedAt, staff.UpdatedAt);

    public static AgentAssignmentView ToView(AgentAssignment assignment) => new(
        assignment.Id, assignment.StaffMemberId, assignment.ProjectId,
        assignment.ParentAssignmentId, assignment.DelegatedByStaffMemberId,
        assignment.ScheduleId, assignment.CreatedByUserId, assignment.Objective,
        assignment.ProfileVersion, assignment.Status.ToString(), assignment.ScheduledFor,
        assignment.PlanSummary, assignment.ResultSummary, assignment.FailureSummary,
        assignment.CreatedAt, assignment.UpdatedAt);

    public static AgentApprovalView ToView(AgentApproval approval) => new(
        approval.Id, approval.AssignmentId, approval.ActionKind, approval.Summary,
        approval.PayloadJson, approval.Status.ToString(), approval.ResolvedByUserId,
        approval.ReviewerComment, approval.RequestedAt, approval.ResolvedAt);
}
