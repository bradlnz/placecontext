namespace PlaceContext.Infrastructure.Persistence;

public sealed class CrmAppointmentRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? CalendarId { get; set; }
    public Guid? ClientId { get; set; }
    public string TitleProtected { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string? LocationProtected { get; set; }
    public string? NotesProtected { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
