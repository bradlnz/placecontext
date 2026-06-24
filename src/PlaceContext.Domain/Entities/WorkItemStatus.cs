namespace PlaceContext.Domain.Entities;

/// <summary>Lifecycle of a queued work item: waiting → claimed by an agent → finished.</summary>
public enum WorkItemStatus
{
    Queued,
    InProgress,
    Done,
}
