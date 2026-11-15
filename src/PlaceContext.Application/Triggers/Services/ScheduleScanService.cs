using PlaceContext.Application.Jobs;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Triggers;
using PlaceContext.Domain.Repositories;

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
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly IJobChainSubmissionQueue? _workflows;

    public ScheduleScanService(
        IJobTriggerRepository triggers, IJobRunQueue queue, ICronSchedule cron,
        ICurrentTenant tenant, IUnitOfWork uow, IClock clock, IJobChainSubmissionQueue? workflows = null)
    {
        _workflows = workflows;
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
        if (due.Count == 0)
        {
            return 0;
        }
        var tenantId = _tenant.TenantId;
        var tz = _tenant.TimeZoneId;

        var fired = 0;
        foreach (var trigger in due)
        {
            var next = trigger.CronExpression is { } cron ? _cron.Next(cron, now, tz) : null;
            if (trigger.WorkflowId is { } workflowId)
            {
                var identity = CurrentUser.Current;
                try
                {
                    if (trigger.ScheduledBy is not { } owner) throw new UnauthorizedAccessException("The workflow schedule has no owner.");
                    CurrentUser.Set(new UserIdentity(owner, owner == PlaceContext.Application.Auth.TrustedServicePrincipals.ManagementApiUserId ? "Owner" : "Viewer"));
                    // Enqueue checks the owner's current permissions; the durable key prevents duplicate ticks after a restart.
                    await (_workflows ?? throw new InvalidOperationException("Workflow scheduling is not configured."))
                        .EnqueueAsync(trigger.ProjectId, workflowId, null, $"schedule:{trigger.Id:N}:{trigger.NextRunAt:O}", ct, maxAttempts: 1);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException)
                {
                    trigger.Disable(now);
                    await _triggers.UpdateAsync(trigger, ct);
                    continue;
                }
                finally { if (identity is null) CurrentUser.Clear(); else CurrentUser.Set(identity); }
            }
            else await _queue.EnqueueAsync(new QueuedJobRun(tenantId, trigger.JobId ?? Guid.Empty, trigger.Id, trigger.Name), ct);
            fired++;
            trigger.MarkFired(now, next);
            await _triggers.UpdateAsync(trigger, ct);
        }

        await _uow.SaveChangesAsync(ct);
        return fired;
    }
}
