using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Tests;

public sealed class CrmAppointmentTests
{
    [Fact]
    public void Appointment_requires_an_end_after_its_start()
    {
        var now = DateTimeOffset.UtcNow;
        var error = Assert.Throws<ArgumentException>(() => CrmAppointment.Create(
            Guid.NewGuid(), null, null, "Consultation", now, now, null, null, Guid.NewGuid(), now));
        Assert.Contains("after", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Appointment_normalizes_customer_facing_fields()
    {
        var now = DateTimeOffset.UtcNow;
        var value = CrmAppointment.Create(Guid.NewGuid(), null, Guid.NewGuid(), "  Site consultation  ",
            now, now.AddHours(1), "  Ossen office  ", "  Bring plans  ", Guid.NewGuid(), now);

        Assert.Equal("Site consultation", value.Title);
        Assert.Equal("Ossen office", value.Location);
        Assert.Equal("Bring plans", value.Notes);
    }

    [Fact]
    public void Appointment_normalizes_offset_times_to_utc_for_postgres()
    {
        var localStart = new DateTimeOffset(2026, 8, 4, 9, 30, 0, TimeSpan.FromHours(10));
        var value = CrmAppointment.Create(Guid.NewGuid(), null, null, "Consultation",
            localStart, localStart.AddHours(1), null, null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(TimeSpan.Zero, value.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, value.EndsAt.Offset);
        Assert.Equal(23, value.StartsAt.Hour);
        Assert.Equal(3, value.StartsAt.Day);
    }

    [Fact]
    public void Appointment_can_be_moved_and_edited()
    {
        var now = DateTimeOffset.UtcNow;
        var calendarId = Guid.NewGuid();
        var value = CrmAppointment.Create(Guid.NewGuid(), null, null, "Consultation",
            now, now.AddHours(1), null, null, Guid.NewGuid(), now);

        value.Update(calendarId, null, "Design review", now.AddDays(1), now.AddDays(1).AddHours(2), "Office", "Updated");

        Assert.Equal(calendarId, value.CalendarId);
        Assert.Equal("Design review", value.Title);
        Assert.Equal("Office", value.Location);
    }
}
