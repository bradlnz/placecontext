using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record ChangeTimelineView(Guid ProjectId, IReadOnlyList<ChangeRecordView> Changes);
