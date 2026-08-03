namespace PlaceContext.Application.Features;

public sealed record CrmAppointmentView(Guid Id, Guid ProjectId, Guid? CalendarId, Guid? ClientId, string? ClientName,
    string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Location,
    string? Notes, DateTimeOffset CreatedAt);
