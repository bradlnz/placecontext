using PlaceContext.Agents.Domain.ValueObjects;
using PlaceContext.Domain.Common;

namespace PlaceContext.Agents.Domain.Entities;

public sealed class AgentAssignment : AggregateRoot
{
    private AgentAssignment() { }

    public Guid Id { get; private set; }
    public Guid StaffMemberId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid? ParentAssignmentId { get; private set; }
    public Guid? DelegatedByStaffMemberId { get; private set; }
    public Guid? ScheduleId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string Objective { get; private set; } = string.Empty;
    public int ProfileVersion { get; private set; }
    public AssignmentStatus Status { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public string PlanSummary { get; private set; } = string.Empty;
    public string ResultSummary { get; private set; } = string.Empty;
    public string FailureSummary { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AgentAssignment Create(Guid staffMemberId, Guid projectId, string objective,
        int profileVersion, Guid createdByUserId, DateTimeOffset now, DateTimeOffset? scheduledFor = null,
        Guid? parentAssignmentId = null, Guid? delegatedByStaffMemberId = null, Guid? scheduleId = null)
    {
        if (staffMemberId == Guid.Empty) throw new ArgumentException("Staff member is required.", nameof(staffMemberId));
        if (projectId == Guid.Empty) throw new ArgumentException("Project is required.", nameof(projectId));
        var cleanObjective = (objective ?? string.Empty).Trim();
        if (cleanObjective.Length is 0 or > 20_000) throw new ArgumentException("Objective must contain 1-20000 characters.", nameof(objective));
        if (profileVersion < 1) throw new ArgumentOutOfRangeException(nameof(profileVersion));
        return new AgentAssignment
        {
            Id = Guid.NewGuid(), StaffMemberId = staffMemberId, ProjectId = projectId,
            Objective = cleanObjective, ProfileVersion = profileVersion,
            CreatedByUserId = createdByUserId, ScheduledFor = scheduledFor,
            ParentAssignmentId = parentAssignmentId, DelegatedByStaffMemberId = delegatedByStaffMemberId,
            ScheduleId = scheduleId, Status = AssignmentStatus.Queued, CreatedAt = now, UpdatedAt = now,
        };
    }

    public static AgentAssignment Rehydrate(Guid id, Guid staffMemberId, Guid projectId,
        Guid? parentAssignmentId, Guid? delegatedByStaffMemberId, Guid? scheduleId,
        Guid createdByUserId, string objective, int profileVersion, AssignmentStatus status,
        DateTimeOffset? scheduledFor, string planSummary, string resultSummary,
        string failureSummary, DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new() { Id = id, StaffMemberId = staffMemberId, ProjectId = projectId,
            ParentAssignmentId = parentAssignmentId, DelegatedByStaffMemberId = delegatedByStaffMemberId,
            ScheduleId = scheduleId, CreatedByUserId = createdByUserId, Objective = objective,
            ProfileVersion = profileVersion, Status = status, ScheduledFor = scheduledFor,
            PlanSummary = planSummary, ResultSummary = resultSummary, FailureSummary = failureSummary,
            CreatedAt = createdAt, UpdatedAt = updatedAt };
}
