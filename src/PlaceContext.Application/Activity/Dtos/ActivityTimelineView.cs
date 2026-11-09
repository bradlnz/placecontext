namespace PlaceContext.Application.Dtos;
public sealed record ActivityTimelineView(Guid ProjectId, IReadOnlyList<ActivityRecordView> Changes);
