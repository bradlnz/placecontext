using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class SaveCrmCalendarHandler : ICommandHandler<SaveCrmCalendarCommand, CrmCalendarView>
{
    private readonly ICrmCalendarRepository _calendars; private readonly ICrmUnitOfWork _uow; private readonly IClock _clock;
    public SaveCrmCalendarHandler(ICrmCalendarRepository calendars, ICrmUnitOfWork uow, IClock clock) => (_calendars, _uow, _clock) = (calendars, uow, clock);
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
