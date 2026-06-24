using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// A unit of work to be done on a project — recorded in the portal (or via MCP) and picked up by an
/// agent. The agent claims the next queued item (<see cref="Claim"/>), works it, then marks it
/// <see cref="Complete"/>. Status-only mutations; the title/detail are fixed once recorded.
/// </summary>
public sealed class WorkItem
{
    private WorkItem(
        Guid id, ProjectId projectId, string title, string? detail, WorkItemPriority priority,
        WorkItemStatus status, DateTimeOffset createdAt, DateTimeOffset? claimedAt, DateTimeOffset? completedAt)
    {
        Id = id;
        ProjectId = projectId;
        Title = title;
        Detail = detail;
        Priority = priority;
        Status = status;
        CreatedAt = createdAt;
        ClaimedAt = claimedAt;
        CompletedAt = completedAt;
    }

    public Guid Id { get; }
    public ProjectId ProjectId { get; }
    public string Title { get; }
    public string? Detail { get; }
    public WorkItemPriority Priority { get; }
    public WorkItemStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public static WorkItem Queue(ProjectId projectId, string title, string? detail, WorkItemPriority priority, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Work item title must not be empty.", nameof(title));

        return new WorkItem(Guid.NewGuid(), projectId, title.Trim(),
            string.IsNullOrWhiteSpace(detail) ? null : detail.Trim(), priority,
            WorkItemStatus.Queued, now, claimedAt: null, completedAt: null);
    }

    public static WorkItem Rehydrate(
        Guid id, ProjectId projectId, string title, string? detail, WorkItemPriority priority,
        WorkItemStatus status, DateTimeOffset createdAt, DateTimeOffset? claimedAt, DateTimeOffset? completedAt)
        => new(id, projectId, title, detail, priority, status, createdAt, claimedAt, completedAt);

    /// <summary>Marks the item as being worked on. Only a queued item can be claimed.</summary>
    public void Claim(DateTimeOffset now)
    {
        if (Status != WorkItemStatus.Queued)
            throw new InvalidOperationException($"Only a queued work item can be claimed (was {Status}).");
        Status = WorkItemStatus.InProgress;
        ClaimedAt = now;
    }

    /// <summary>Marks the item finished.</summary>
    public void Complete(DateTimeOffset now)
    {
        Status = WorkItemStatus.Done;
        CompletedAt = now;
    }
}
