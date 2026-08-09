using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record ActivityTimelineView(Guid ProjectId, IReadOnlyList<ActivityRecordView> Changes);
