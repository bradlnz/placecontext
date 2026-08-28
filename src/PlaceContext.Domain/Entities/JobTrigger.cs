using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: an automated trigger that starts runs of a <see cref="Job"/>. A trigger fires either
/// on a recurring cron <see cref="TriggerKind.Schedule"/> or in reaction to a named
/// <see cref="TriggerKind.Event"/>. Firing simply enqueues a job run (concurrent runs are allowed).
///
/// Next-run computation for schedules is not a domain concern — the cron string is stored here and the
/// Application/Infrastructure supplies the computed <see cref="NextRunAt"/> via <see cref="MarkFired"/>
/// and <see cref="Reschedule"/>.
/// </summary>
public sealed class JobTrigger
{
    private JobTrigger(
        Guid id,
        Guid projectId,
        Guid? jobId,
        string name,
        TriggerKind kind,
        bool enabled,
        string? cronExpression,
        string? eventName,
        DateTimeOffset? nextRunAt,
        DateTimeOffset? lastFiredAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        ProjectId = projectId;
        JobId = jobId;
        Name = name;
        Kind = kind;
        Enabled = enabled;
        CronExpression = cronExpression;
        EventName = eventName;
        NextRunAt = nextRunAt;
        LastFiredAt = lastFiredAt;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }

    /// <summary>The job this trigger runs.</summary>
    public Guid? JobId { get; }

    /// <summary>Human-readable label for this trigger.</summary>
    public string Name { get; private set; }

    public TriggerKind Kind { get; }

    /// <summary>When false, the trigger never fires (paused).</summary>
    public bool Enabled { get; private set; }

    /// <summary>Cron expression (schedule triggers only); null for event triggers.</summary>
    public string? CronExpression { get; private set; }

    /// <summary>Subscribed event name (event triggers only); null for schedule triggers.</summary>
    public string? EventName { get; private set; }

    /// <summary>Next time this schedule is due to fire (UTC); null for event triggers or when paused.</summary>
    public DateTimeOffset? NextRunAt { get; private set; }

    /// <summary>Last time this trigger fired a run (UTC); null if it has never fired.</summary>
    public DateTimeOffset? LastFiredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>True when the schedule is due at <paramref name="now"/> (enabled cron-based trigger with a past NextRunAt).</summary>
    public bool IsDue(DateTimeOffset now) =>
        Enabled && Kind == TriggerKind.Schedule && NextRunAt is { } next && next <= now;

    /// <summary>True when this enabled event trigger subscribes to <paramref name="eventName"/>.</summary>
    public bool MatchesEvent(string eventName) =>
        Enabled && Kind == TriggerKind.Event &&
        string.Equals(EventName, eventName, StringComparison.OrdinalIgnoreCase);

    // ── Factories ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Creates a cron-schedule trigger. <paramref name="nextRunAt"/> is the first computed fire time.</summary>
    public static JobTrigger CreateSchedule(
        Guid projectId, Guid jobId, string name, string cronExpression,
        DateTimeOffset? nextRunAt, DateTimeOffset now)
    {
        ValidateIds(projectId, jobId);
        ValidateName(name);
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("A schedule trigger requires a cron expression.", nameof(cronExpression));

        return new JobTrigger(
            Guid.NewGuid(), projectId, jobId, name.Trim(), TriggerKind.Schedule,
            enabled: true, cronExpression: cronExpression.Trim(), eventName: null,
            nextRunAt: nextRunAt, lastFiredAt: null, createdAt: now, updatedAt: now);
    }

    /// <summary>Creates an event-subscription trigger.</summary>
    public static JobTrigger CreateEvent(
        Guid projectId, Guid jobId, string name, string eventName, DateTimeOffset now)
    {
        ValidateIds(projectId, jobId);
        ValidateName(name);
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("An event trigger requires an event name.", nameof(eventName));

        return new JobTrigger(
            Guid.NewGuid(), projectId, jobId, name.Trim(), TriggerKind.Event,
            enabled: true, cronExpression: null, eventName: eventName.Trim(),
            nextRunAt: null, lastFiredAt: null, createdAt: now, updatedAt: now);
    }

    /// <summary>Rehydrates from persisted state. Infrastructure only.</summary>
    public static JobTrigger Rehydrate(
        Guid id, Guid projectId, Guid? jobId, string name, TriggerKind kind, bool enabled,
        string? cronExpression, string? eventName,
        DateTimeOffset? nextRunAt, DateTimeOffset? lastFiredAt,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(id, projectId, jobId, name, kind, enabled, cronExpression, eventName,
               nextRunAt, lastFiredAt, createdAt, updatedAt);

    // ── Behaviour ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Records that the trigger fired at <paramref name="firedAt"/>, advancing the schedule to
    /// <paramref name="nextRunAt"/> (pass null for event triggers).</summary>
    public void MarkFired(DateTimeOffset firedAt, DateTimeOffset? nextRunAt)
    {
        LastFiredAt = firedAt;
        if (Kind == TriggerKind.Schedule)
            NextRunAt = nextRunAt;
        UpdatedAt = firedAt;
    }

    /// <summary>Updates a schedule's cron expression and its recomputed next-run time.</summary>
    public void Reschedule(string cronExpression, DateTimeOffset? nextRunAt, DateTimeOffset now)
    {
        if (Kind != TriggerKind.Schedule)
            throw new InvalidOperationException("Only schedule triggers can be rescheduled.");
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("A schedule trigger requires a cron expression.", nameof(cronExpression));

        CronExpression = cronExpression.Trim();
        NextRunAt = nextRunAt;
        UpdatedAt = now;
    }

    /// <summary>Renames the trigger.</summary>
    public void Rename(string name, DateTimeOffset now)
    {
        ValidateName(name);
        Name = name.Trim();
        UpdatedAt = now;
    }

    /// <summary>Changes the event name this trigger subscribes to.</summary>
    public void RenameEvent(string eventName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name must not be empty.", nameof(eventName));
        EventName = eventName.Trim();
        UpdatedAt = now;
    }

    /// <summary>Enables the trigger. For schedules, supply the recomputed next-run time.</summary>
    public void Enable(DateTimeOffset? nextRunAt, DateTimeOffset now)
    {
        Enabled = true;
        if (Kind == TriggerKind.Schedule)
            NextRunAt = nextRunAt;
        UpdatedAt = now;
    }

    /// <summary>Pauses the trigger so it never fires until re-enabled.</summary>
    public void Disable(DateTimeOffset now)
    {
        Enabled = false;
        NextRunAt = null;
        UpdatedAt = now;
    }

    private static void ValidateIds(Guid projectId, Guid jobId)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        if (jobId == Guid.Empty)
            throw new ArgumentException("JobId must not be empty.", nameof(jobId));
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Trigger name must not be empty.", nameof(name));
    }
}
