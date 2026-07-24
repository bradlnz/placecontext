using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Records an event occurrence and fans it out to every enabled event-trigger subscribed to its name,
/// enqueuing a job run for each. Shared by <see cref="EmitEventHandler"/> (user events) and the
/// built-in domain-event emission points (activity recorded, job completed).
///
/// Firing only enqueues — the background runner executes the job — so emitting an event never blocks
/// on container execution. Scoped: shares the request's DbContext/UoW.
/// </summary>
public sealed class EventDispatchService
{
    private readonly IEventRepository _events;
    private readonly IJobTriggerRepository _triggers;
    private readonly IJobRunQueue _queue;
    private readonly ICurrentTenant _tenant;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public EventDispatchService(
        IEventRepository events, IJobTriggerRepository triggers, IJobRunQueue queue,
        ICurrentTenant tenant, IUnitOfWork uow, IClock clock)
    {
        _events = events;
        _triggers = triggers;
        _queue = queue;
        _tenant = tenant;
        _uow = uow;
        _clock = clock;
    }

    /// <summary>Records a user-emitted event and fans it out to subscribed triggers.</summary>
    public Task<EventOccurrenceView> EmitAsync(string name, Guid? projectId, string? payload, CancellationToken ct = default)
        => DispatchAsync(EventOccurrence.Emit(name, projectId, payload, _clock.UtcNow), ct);

    /// <summary>Records a system-raised domain event and fans it out to subscribed triggers.</summary>
    public Task<EventOccurrenceView> RaiseAsync(string name, Guid? projectId, string? payload, CancellationToken ct = default)
        => DispatchAsync(EventOccurrence.Raise(name, projectId, payload, _clock.UtcNow), ct);

    private async Task<EventOccurrenceView> DispatchAsync(EventOccurrence occurrence, CancellationToken ct)
    {
        await _events.AddOccurrenceAsync(occurrence, ct);

        var matching = await _triggers.ListForEventAsync(occurrence.Name, ct);
        var firedAt = _clock.UtcNow;
        var tenantId = _tenant.TenantId;
        var fired = 0;

        foreach (var trigger in matching)
        {
            trigger.MarkFired(firedAt, nextRunAt: null);
            await _triggers.UpdateAsync(trigger, ct); // re-finds the row by id and copies the new state
            // The event payload is injected as parameters for the triggered run (task: param injection).
            await _queue.EnqueueAsync(new QueuedJobRun(tenantId, trigger.JobId ?? Guid.Empty, trigger.Id, trigger.Name, occurrence.Payload), ct);
            fired++;
        }

        await _uow.SaveChangesAsync(ct);
        return ToView(occurrence, fired);
    }

    internal static EventOccurrenceView ToView(EventOccurrence o, int triggeredRuns) => new(
        Id: o.Id,
        Name: o.Name,
        Source: o.Source.ToString(),
        ProjectId: o.ProjectId,
        Payload: o.Payload,
        OccurredAt: o.OccurredAt,
        TriggeredRuns: triggeredRuns);
}
