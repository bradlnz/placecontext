using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Jobs.Domain.Persistence;

namespace PlaceContext.Application.Features;

/// <summary>
/// Fires all due cron-schedule triggers for the current tenant: enqueues a job run for each, advances
/// its NextRunAt via <see cref="ICronSchedule"/>, and records the fire. Invoked once per tenant by the
/// background scheduler (which establishes the ambient tenant first). Scoped: shares the tenant scope's
/// DbContext/UoW.
/// </summary>
public sealed class ScheduleScanService
{
    private readonly IJobTriggerRepository _triggers;
    private readonly IJobRunQueue _queue;
    private readonly ICronSchedule _cron;
    private readonly ICurrentTenant _tenant;
    private readonly IJobsUnitOfWork _uow;
    private readonly IClock _clock;

    public ScheduleScanService(
        IJobTriggerRepository triggers, IJobRunQueue queue, ICronSchedule cron,
        ICurrentTenant tenant, IJobsUnitOfWork uow, IClock clock)
    {
        _triggers = triggers;
        _queue = queue;
        _cron = cron;
        _tenant = tenant;
        _uow = uow;
        _clock = clock;
    }

    /// <summary>Fires every due schedule for the current tenant. Returns how many runs were enqueued.</summary>
    public async Task<int> FireDueAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var due = await _triggers.ListDueSchedulesAsync(now, ct);
        if (due.Count == 0) return 0;

        var tenantId = _tenant.TenantId;
        var tz = _tenant.TimeZoneId;

        foreach (var trigger in due)
        {
            var next = trigger.CronExpression is { } cron ? _cron.Next(cron, now, tz) : null;
            trigger.MarkFired(now, next);
            await _triggers.UpdateAsync(trigger, ct);
            await _queue.EnqueueAsync(new QueuedJobRun(tenantId, trigger.JobId ?? Guid.Empty, trigger.Id, trigger.Name), ct);
        }

        await _uow.SaveChangesAsync(ct);
        return due.Count;
    }
}
