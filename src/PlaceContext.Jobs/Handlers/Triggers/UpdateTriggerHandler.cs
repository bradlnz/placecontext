using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class UpdateTriggerHandler : ICommandHandler<UpdateTriggerCommand, TriggerView>
{
    private readonly IJobTriggerRepository _triggers;
    private readonly ICronSchedule _cron;
    private readonly ICurrentTenant _tenant;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public UpdateTriggerHandler(
        IJobTriggerRepository triggers,
        ICronSchedule cron,
        ICurrentTenant tenant,
        IUnitOfWork uow,
        IClock clock)
    {
        _triggers = triggers;
        _cron = cron;
        _tenant = tenant;
        _uow = uow;
        _clock = clock;
    }

    public async Task<TriggerView> HandleAsync(UpdateTriggerCommand command, CancellationToken ct = default)
    {
        var trigger = await _triggers.GetByIdAsync(command.TriggerId, ct)
            ?? throw new InvalidOperationException($"Trigger {command.TriggerId} not found.");

        var now = _clock.UtcNow;

        if (command.Name is not null)
            trigger.Rename(command.Name, now);

        if (command.CronExpression is not null)
        {
            var cron = command.CronExpression.Trim();
            if (!_cron.IsValid(cron))
                throw new ArgumentException($"'{cron}' is not a valid cron expression.");
            var next = _cron.Next(cron, now, _tenant.TimeZoneId);
            trigger.Reschedule(cron, next, now);
        }

        if (command.EventName is not null)
        {
            if (string.IsNullOrWhiteSpace(command.EventName))
                throw new ArgumentException("Event name must not be empty.");
            trigger.RenameEvent(command.EventName.Trim(), now);
        }

        if (command.Enabled is { } enabled)
        {
            if (enabled)
            {
                var next = trigger.Kind is PlaceContext.Domain.ValueObjects.TriggerKind.Schedule
                           or PlaceContext.Domain.ValueObjects.TriggerKind.Launchpad
                           or PlaceContext.Domain.ValueObjects.TriggerKind.Command
                    ? _cron.Next(trigger.CronExpression!, now, _tenant.TimeZoneId)
                    : (DateTimeOffset?)null;
                trigger.Enable(next, now);
            }
            else
            {
                trigger.Disable(now);
            }
        }

        await _uow.SaveChangesAsync(ct);
        return TriggerViewMapper.ToView(trigger);
    }
}
