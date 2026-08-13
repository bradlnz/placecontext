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
    private readonly CrmUserScope _scope;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateCrmAppointmentHandler(
        ICrmAppointmentRepository appointments,
        ICrmClientRepository clients,
        ICrmCalendarRepository calendars,
        CrmUserScope scope,
        ICurrentUser currentUser,
        IUnitOfWork uow,
        IClock clock)
        => (_appointments, _clients, _calendars, _scope, _currentUser, _uow, _clock)
            = (appointments, clients, calendars, scope, currentUser, uow, clock);

    public async Task<CrmAppointmentView> HandleAsync(CreateCrmAppointmentCommand command, CancellationToken ct = default)
    {
        CrmClient? client = null;
        if (command.ClientId is { } clientId)
        {
            client = await _clients.GetByIdAsync(clientId, ct)
                ?? throw new InvalidOperationException("The selected CRM contact no longer exists.");
            if (client.ProjectId != command.ProjectId)
                throw new InvalidOperationException("The selected contact belongs to another project.");
            await _scope.EnsureClientAccessAsync(client.ProjectId, client.Id, ct);
        }
        else
        {
            await _scope.EnsureClientAccessAsync(command.ProjectId, Guid.Empty, ct);
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
            if (value.ClientId is { } existingClientId)
                await _scope.EnsureClientAccessAsync(value.ProjectId, existingClientId, ct);
            else
                await _scope.EnsureClientAccessAsync(value.ProjectId, Guid.Empty, ct);
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
    private readonly ICrmAppointmentRepository _appointments;
    private readonly CrmUserScope _scope;
    private readonly IUnitOfWork _uow;

    public DeleteCrmAppointmentHandler(
        ICrmAppointmentRepository appointments,
        CrmUserScope scope,
        IUnitOfWork uow)
        => (_appointments, _scope, _uow) = (appointments, scope, uow);

    public async Task<bool> HandleAsync(DeleteCrmAppointmentCommand command, CancellationToken ct = default)
    {
        var value = await _appointments.GetByIdAsync(command.AppointmentId, ct);
        if (value is null) return false;
        if (value.ClientId is { } clientId)
            await _scope.EnsureClientAccessAsync(value.ProjectId, clientId, ct);
        else
            await _scope.EnsureClientAccessAsync(value.ProjectId, Guid.Empty, ct);
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
    private readonly ICrmCalendarRepository _calendars;
    private readonly ICrmClientRepository _clients;
    private readonly ICrmClientUserAssignmentRepository _assignments;
    private readonly ICrmUserRepository _crmUsers;
    private readonly ICrmAppointmentRepository _appointments;
    private readonly CrmUserScope _scope;
    private readonly ICurrentUser _currentUser;

    public ListCrmCalendarsHandler(
        ICrmCalendarRepository calendars,
        ICrmClientRepository clients,
        ICrmClientUserAssignmentRepository assignments,
        ICrmUserRepository crmUsers,
        ICrmAppointmentRepository appointments,
        CrmUserScope scope,
        ICurrentUser currentUser)
        => (_calendars, _clients, _assignments, _crmUsers, _appointments, _scope, _currentUser)
            = (calendars, clients, assignments, crmUsers, appointments, scope, currentUser);

    public async Task<IReadOnlyList<CrmCalendarView>> HandleAsync(ListCrmCalendarsQuery query, CancellationToken ct = default)
    {
        var calendars = (await _calendars.ListForProjectAsync(query.ProjectId, ct))
            .Select(SaveCrmCalendarHandler.Map)
            .ToArray();

        if (!string.Equals(_currentUser.Role, UserRole.CrmUser.ToString(), StringComparison.OrdinalIgnoreCase))
            return calendars;

        var clients = await _clients.ListForProjectAsync(query.ProjectId, ct);
        var allowedClientIds = (await _scope.FilterByAccessAsync(
                query.ProjectId,
                clients,
                client => client.Id,
                ct))
            .Select(client => client.Id)
            .ToHashSet();
        if (allowedClientIds.Count == 0)
            return Array.Empty<CrmCalendarView>();

        var linkedCrmUserIds = new HashSet<Guid>();
        foreach (var clientId in allowedClientIds)
        {
            foreach (var crmUserId in await _assignments.ListForClientAsync(query.ProjectId, clientId, ct))
                linkedCrmUserIds.Add(crmUserId);
        }

        if (linkedCrmUserIds.Count == 0)
            return Array.Empty<CrmCalendarView>();

        var linkedAuthUserIds = (await _crmUsers.ListForProjectAsync(query.ProjectId, ct))
            .Where(user => linkedCrmUserIds.Contains(user.Id) && user.AuthUserId is not null)
            .Select(user => user.AuthUserId!.Value)
            .ToHashSet();
        if (linkedAuthUserIds.Count == 0)
            return Array.Empty<CrmCalendarView>();

        var allowedCalendarIds = (await _appointments.ListForProjectAsync(query.ProjectId, ct))
            .Where(appointment => linkedAuthUserIds.Contains(appointment.CreatedByUserId) && appointment.CalendarId is not null)
            .Select(appointment => appointment.CalendarId!.Value)
            .Distinct()
            .ToHashSet();

        if (allowedCalendarIds.Count == 0)
            return Array.Empty<CrmCalendarView>();

        return calendars.Where(calendar => allowedCalendarIds.Contains(calendar.Id)).ToArray();
    }
}

public sealed class ListCrmAppointmentsHandler
    : IQueryHandler<ListCrmAppointmentsQuery, IReadOnlyList<CrmAppointmentView>>
{
    private readonly ICrmAppointmentRepository _appointments;
    private readonly ICrmClientRepository _clients;
    private readonly CrmUserScope _scope;

    public ListCrmAppointmentsHandler(
        ICrmAppointmentRepository appointments,
        ICrmClientRepository clients,
        CrmUserScope scope)
        => (_appointments, _clients, _scope) = (appointments, clients, scope);

    public async Task<IReadOnlyList<CrmAppointmentView>> HandleAsync(ListCrmAppointmentsQuery query, CancellationToken ct = default)
    {
        var clients = (await _clients.ListForProjectAsync(query.ProjectId, ct)).ToDictionary(x => x.Id, x => x.Name);
        var values = await _scope.FilterByAccessAsync(
            query.ProjectId,
            await _appointments.ListForProjectAsync(query.ProjectId, ct),
            value => value.ClientId ?? Guid.Empty,
            ct);
        return values
            .Select(value => CreateCrmAppointmentHandler.Map(value,
                value.ClientId is { } id && clients.TryGetValue(id, out var name) ? name : null))
            .ToArray();
    }
}
