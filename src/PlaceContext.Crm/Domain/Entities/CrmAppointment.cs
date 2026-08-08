namespace PlaceContext.Domain.Entities;

public sealed class CrmAppointment
{
    private CrmAppointment(Guid id, Guid projectId, Guid? calendarId, Guid? clientId, string title,
        DateTimeOffset startsAt, DateTimeOffset endsAt, string? location, string? notes,
        Guid createdByUserId, DateTimeOffset createdAt)
        => (Id, ProjectId, CalendarId, ClientId, Title, StartsAt, EndsAt, Location, Notes, CreatedByUserId, CreatedAt)
            = (id, projectId, calendarId, clientId, title, startsAt, endsAt, location, notes, createdByUserId, createdAt);

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public Guid? CalendarId { get; private set; }
    public Guid? ClientId { get; private set; }
    public string Title { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public string? Location { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedByUserId { get; }
    public DateTimeOffset CreatedAt { get; }

    public static CrmAppointment Create(Guid projectId, Guid? calendarId, Guid? clientId, string title,
        DateTimeOffset startsAt, DateTimeOffset endsAt, string? location, string? notes,
        Guid createdByUserId, DateTimeOffset now)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Appointment title is required.", nameof(title));
        if (endsAt <= startsAt) throw new ArgumentException("Appointment end must be after its start.", nameof(endsAt));
        return new(Guid.NewGuid(), projectId, calendarId, clientId, title.Trim(), startsAt.ToUniversalTime(), endsAt.ToUniversalTime(),
            Clean(location), Clean(notes), createdByUserId, now);
    }

    public static CrmAppointment Rehydrate(Guid id, Guid projectId, Guid? calendarId, Guid? clientId, string title,
        DateTimeOffset startsAt, DateTimeOffset endsAt, string? location, string? notes,
        Guid createdByUserId, DateTimeOffset createdAt)
        => new(id, projectId, calendarId, clientId, title, startsAt, endsAt, location, notes, createdByUserId, createdAt);

    public void Update(Guid? calendarId, Guid? clientId, string title, DateTimeOffset startsAt,
        DateTimeOffset endsAt, string? location, string? notes)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Appointment title is required.", nameof(title));
        if (endsAt <= startsAt) throw new ArgumentException("Appointment end must be after its start.", nameof(endsAt));
        CalendarId = calendarId;
        ClientId = clientId;
        Title = title.Trim();
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt.ToUniversalTime();
        Location = Clean(location);
        Notes = Clean(notes);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
