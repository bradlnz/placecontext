namespace PlaceContext.Application.Features;
public sealed record CrmCalendarView(Guid Id, Guid ProjectId, string Name, string Color,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
