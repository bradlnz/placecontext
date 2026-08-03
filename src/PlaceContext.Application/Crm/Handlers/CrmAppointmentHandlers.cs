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
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateCrmAppointmentHandler(ICrmAppointmentRepository appointments,
        ICrmClientRepository clients, ICrmCalendarRepository calendars, ICurrentUser currentUser, IUnitOfWork uow, IClock clock)
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

public sealed class DeleteCrmAppointmentHandler : ICommandHandler<DeleteCrmAppointmentCommand, bool>
{
    private readonly ICrmAppointmentRepository _appointments; private readonly IUnitOfWork _uow;
    public DeleteCrmAppointmentHandler(ICrmAppointmentRepository appointments, IUnitOfWork uow) => (_appointments, _uow) = (appointments, uow);
    public async Task<bool> HandleAsync(DeleteCrmAppointmentCommand command, CancellationToken ct = default)
    {
        if (await _appointments.GetByIdAsync(command.AppointmentId, ct) is null) return false;
        await _appointments.DeleteAsync(command.AppointmentId, ct); await _uow.SaveChangesAsync(ct); return true;
    }
}

public sealed class SaveCrmCalendarHandler : ICommandHandler<SaveCrmCalendarCommand, CrmCalendarView>
{
    private readonly ICrmCalendarRepository _calendars; private readonly IUnitOfWork _uow; private readonly IClock _clock;
    public SaveCrmCalendarHandler(ICrmCalendarRepository calendars, IUnitOfWork uow, IClock clock) => (_calendars, _uow, _clock) = (calendars, uow, clock);
    public async Task<CrmCalendarView> HandleAsync(SaveCrmCalendarCommand command, CancellationToken ct = default)
    {
        CrmCalendar value;
        if (command.CalendarId is { } id)
        {
            value = await _calendars.GetByIdAsync(id, ct) ?? throw new InvalidOperationException("The calendar no longer exists.");
            if (value.ProjectId != command.ProjectId) throw new InvalidOperationException("The calendar belongs to another project.");
            value.Update(command.Name, command.Color, _clock.UtcNow); await _calendars.UpdateAsync(value, ct);
        }
        else { value = CrmCalendar.Create(command.ProjectId, command.Name, command.Color, _clock.UtcNow); await _calendars.AddAsync(value, ct); }
        await _uow.SaveChangesAsync(ct); return Map(value);
    }
    internal static CrmCalendarView Map(CrmCalendar value) => new(value.Id, value.ProjectId, value.Name, value.Color, value.CreatedAt, value.UpdatedAt);
}

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

public sealed class ListCrmCalendarsHandler : IQueryHandler<ListCrmCalendarsQuery, IReadOnlyList<CrmCalendarView>>
{
    private readonly ICrmCalendarRepository _calendars; public ListCrmCalendarsHandler(ICrmCalendarRepository calendars) => _calendars = calendars;
    public async Task<IReadOnlyList<CrmCalendarView>> HandleAsync(ListCrmCalendarsQuery query, CancellationToken ct = default)
        => (await _calendars.ListForProjectAsync(query.ProjectId, ct)).Select(SaveCrmCalendarHandler.Map).ToArray();
}

public sealed class ListCrmAppointmentsHandler
    : IQueryHandler<ListCrmAppointmentsQuery, IReadOnlyList<CrmAppointmentView>>
{
    private readonly ICrmAppointmentRepository _appointments;
    private readonly ICrmClientRepository _clients;
    public ListCrmAppointmentsHandler(ICrmAppointmentRepository appointments, ICrmClientRepository clients)
        => (_appointments, _clients) = (appointments, clients);

    public async Task<IReadOnlyList<CrmAppointmentView>> HandleAsync(ListCrmAppointmentsQuery query, CancellationToken ct = default)
    {
        var clients = (await _clients.ListForProjectAsync(query.ProjectId, ct)).ToDictionary(x => x.Id, x => x.Name);
        return (await _appointments.ListForProjectAsync(query.ProjectId, ct))
            .Select(value => CreateCrmAppointmentHandler.Map(value,
                value.ClientId is { } id && clients.TryGetValue(id, out var name) ? name : null))
            .ToArray();
    }
}
