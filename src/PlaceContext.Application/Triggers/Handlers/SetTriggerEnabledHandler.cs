using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Triggers;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class SetTriggerEnabledHandler : ICommandHandler<SetTriggerEnabledCommand, TriggerView>
{
    private readonly IJobTriggerRepository _triggers;
    private readonly ICronSchedule _cron;
    private readonly ICurrentTenant _tenant;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public SetTriggerEnabledHandler(
        IJobTriggerRepository triggers, ICronSchedule cron, ICurrentTenant tenant, IUnitOfWork uow, IClock clock)
    {
        _triggers = triggers;
        _cron = cron;
        _tenant = tenant;
        _uow = uow;
        _clock = clock;
    }

    public async Task<TriggerView> HandleAsync(SetTriggerEnabledCommand command, CancellationToken ct = default)
    {
        var trigger = await _triggers.GetByIdAsync(command.TriggerId, ct)
            ?? throw new InvalidOperationException($"Trigger {command.TriggerId} not found.");

        var now = _clock.UtcNow;
        void ApplyEnabledState()
        {
            if (command.Enabled)
            {
                DateTimeOffset? next = trigger.Kind == TriggerKind.Schedule && trigger.CronExpression is { } cron
                    ? _cron.Next(cron, now, _tenant.TimeZoneId)
                    : null;
                trigger.Enable(next, now);
                return;
            }
            trigger.Disable(now);
        }
        ApplyEnabledState();
        await _triggers.UpdateAsync(trigger, ct);
        await _uow.SaveChangesAsync(ct);
        return TriggerViewMapper.ToView(trigger);
    }
}
