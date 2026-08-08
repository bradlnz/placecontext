namespace PlaceContext.Agents.Contracts.Dtos;

public sealed record AgentsWorkspaceView(
    IReadOnlyList<AgentProfileView> Profiles,
    IReadOnlyList<StaffMemberView> Staff,
    IReadOnlyList<AgentAssignmentView> Assignments,
    IReadOnlyList<AgentApprovalView> Approvals);
