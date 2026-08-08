namespace PlaceContext.Agents.Domain.ValueObjects;

public enum AssignmentStatus
{
    Draft,
    Queued,
    Planning,
    AwaitingApproval,
    Running,
    Blocked,
    Completed,
    Failed,
    Cancelled,
}
