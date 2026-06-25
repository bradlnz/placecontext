namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one work-queue item.</summary>
public sealed record WorkItemView(
    Guid Id, Guid ProjectId, string Title, string? Detail, string Priority, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? ClaimedAt, DateTimeOffset? CompletedAt);
