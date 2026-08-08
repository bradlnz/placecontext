using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteCrmCalendarHandler : ICommandHandler<DeleteCrmCalendarCommand, bool>
{
    private readonly ICrmCalendarRepository _calendars; private readonly ICrmAppointmentRepository _appointments; private readonly IUnitOfWork _uow;
    public DeleteCrmCalendarHandler(ICrmCalendarRepository calendars, ICrmAppointmentRepository appointments, IUnitOfWork uow) => (_calendars, _appointments, _uow) = (calendars, appointments, uow);
    public async Task<bool> HandleAsync(DeleteCrmCalendarCommand command, CancellationToken ct = default)
    {
        var calendar = await _calendars.GetByIdAsync(command.CalendarId, ct); if (calendar is null) return false;
        foreach (var appointment in (await _appointments.ListForProjectAsync(calendar.ProjectId, ct)).Where(x => x.CalendarId == calendar.Id))
        { appointment.Update(null, appointment.ClientId, appointment.Title, appointment.StartsAt, appointment.EndsAt, appointment.Location, appointment.Notes); await _appointments.UpdateAsync(appointment, ct); }
        await _calendars.DeleteAsync(calendar.Id, ct); await _uow.SaveChangesAsync(ct); return true;
    }
}
