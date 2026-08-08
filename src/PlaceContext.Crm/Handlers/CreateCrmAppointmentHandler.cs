using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class CreateCrmAppointmentHandler
    : ICommandHandler<CreateCrmAppointmentCommand, CrmAppointmentView>
{
    private readonly ICrmAppointmentRepository _appointments;
    private readonly ICrmClientRepository _clients;
    private readonly ICrmCalendarRepository _calendars;
    private readonly ICurrentUser _currentUser;
    private readonly ICrmUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateCrmAppointmentHandler(ICrmAppointmentRepository appointments,
        ICrmClientRepository clients, ICrmCalendarRepository calendars, ICurrentUser currentUser, ICrmUnitOfWork uow, IClock clock)
        => (_appointments, _clients, _calendars, _currentUser, _uow, _clock)
            = (appointments, clients, calendars, currentUser, uow, clock);

    public async Task<CrmAppointmentView> HandleAsync(CreateCrmAppointmentCommand command, CancellationToken ct = default)
    {
        CrmClient? client = null;
        if (command.ClientId is { } clientId)
        {
            client = await _clients.GetByIdAsync(clientId, ct)
                ?? throw new InvalidOperationException("The selected CRM contact no longer exists.");
            if (client.ProjectId != command.ProjectId)
                throw new InvalidOperationException("The selected contact belongs to another project.");
        }

        if (command.CalendarId is { } calendarId)
        {
            var calendar = await _calendars.GetByIdAsync(calendarId, ct)
                ?? throw new InvalidOperationException("The selected calendar no longer exists.");
            if (calendar.ProjectId != command.ProjectId)
                throw new InvalidOperationException("The selected calendar belongs to another project.");
        }

        CrmAppointment value;
        if (command.AppointmentId is { } appointmentId)
        {
            value = await _appointments.GetByIdAsync(appointmentId, ct)
                ?? throw new InvalidOperationException("The appointment no longer exists.");
            if (value.ProjectId != command.ProjectId) throw new InvalidOperationException("The appointment belongs to another project.");
            value.Update(command.CalendarId, command.ClientId, command.Title, command.StartsAt,
                command.EndsAt, command.Location, command.Notes);
            await _appointments.UpdateAsync(value, ct);
        }
        else
        {
            value = CrmAppointment.Create(command.ProjectId, command.CalendarId, command.ClientId, command.Title,
                command.StartsAt, command.EndsAt, command.Location, command.Notes,
                _currentUser.UserId, _clock.UtcNow);
            await _appointments.AddAsync(value, ct);
        }
        await _uow.SaveChangesAsync(ct);
        return Map(value, client?.Name);
    }

    internal static CrmAppointmentView Map(CrmAppointment value, string? clientName) => new(
        value.Id, value.ProjectId, value.CalendarId, value.ClientId, clientName, value.Title, value.StartsAt,
        value.EndsAt, value.Location, value.Notes, value.CreatedAt);
}
