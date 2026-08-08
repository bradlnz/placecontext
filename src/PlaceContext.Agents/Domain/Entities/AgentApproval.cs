using PlaceContext.Agents.Domain.ValueObjects;
using PlaceContext.Domain.Common;

namespace PlaceContext.Agents.Domain.Entities;

public sealed class AgentApproval : AggregateRoot
{
    private AgentApproval() { }

    public Guid Id { get; private set; }
    public Guid AssignmentId { get; private set; }
    public string ActionKind { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = "{}";
    public ApprovalStatus Status { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public string ReviewerComment { get; private set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public void Resolve(ApprovalDecision decision, Guid userId, string? comment, DateTimeOffset now)
    {
        if (Status != ApprovalStatus.Pending) throw new InvalidOperationException("Approval has already been resolved.");
        Status = decision switch
        {
            ApprovalDecision.Approve => ApprovalStatus.Approved,
            ApprovalDecision.Reject => ApprovalStatus.Rejected,
            ApprovalDecision.Return => ApprovalStatus.Returned,
            _ => throw new ArgumentOutOfRangeException(nameof(decision)),
        };
        ResolvedByUserId = userId;
        ReviewerComment = (comment ?? string.Empty).Trim();
        ResolvedAt = now;
    }

    public static AgentApproval Rehydrate(Guid id, Guid assignmentId, string actionKind,
        string summary, string payloadJson, ApprovalStatus status, Guid? resolvedByUserId,
        string reviewerComment, DateTimeOffset requestedAt, DateTimeOffset? resolvedAt)
        => new() { Id = id, AssignmentId = assignmentId, ActionKind = actionKind,
            Summary = summary, PayloadJson = payloadJson, Status = status,
            ResolvedByUserId = resolvedByUserId, ReviewerComment = reviewerComment,
            RequestedAt = requestedAt, ResolvedAt = resolvedAt };
}
