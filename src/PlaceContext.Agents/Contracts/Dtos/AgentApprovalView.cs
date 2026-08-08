namespace PlaceContext.Agents.Contracts.Dtos;

public sealed record AgentApprovalView(
    Guid Id, Guid AssignmentId, string ActionKind, string Summary,
    string PayloadJson, string Status, Guid? ResolvedByUserId,
    string ReviewerComment, DateTimeOffset RequestedAt, DateTimeOffset? ResolvedAt);
