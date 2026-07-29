using PlaceContext.Domain.Common;
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
public sealed class JobTrigger : AggregateRoot
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
        Guid? chainId,
        string? sourceTable,
        string? prompt,
        Guid? commandId,
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
        ChainId = chainId;
        SourceTable = sourceTable;
        Prompt = prompt;
        CommandId = commandId;
        NextRunAt = nextRunAt;
        LastFiredAt = lastFiredAt;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }

    /// <summary>The job a schedule/event trigger runs. Null for launchpads (they target a chain).</summary>
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

    /// <summary>The job chain a launchpad's agent session is pointed at (launchpads only).</summary>
    public Guid? ChainId { get; }

    /// <summary>Project data table fetched into the launchpad session context (launchpads only; optional).</summary>
    public string? SourceTable { get; }

    /// <summary>The operator-defined prompt the launchpad session runs autonomously (launchpads only).</summary>
    public string? Prompt { get; }

    /// <summary>Optional command to run when this trigger fires (command triggers).</summary>
    public Guid? CommandId { get; }

    /// <summary>Next time this schedule is due to fire (UTC); null for event triggers or when paused.</summary>
    public DateTimeOffset? NextRunAt { get; private set; }

    /// <summary>Last time this trigger fired a run (UTC); null if it has never fired.</summary>
    public DateTimeOffset? LastFiredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>True when the schedule is due at <paramref name="now"/> (enabled cron-based trigger with a past NextRunAt).</summary>
    public bool IsDue(DateTimeOffset now) =>
        Enabled && (Kind is TriggerKind.Schedule or TriggerKind.Launchpad or TriggerKind.Command) && NextRunAt is { } next && next <= now;

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
            chainId: null, sourceTable: null, prompt: null, commandId: null,
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
            chainId: null, sourceTable: null, prompt: null, commandId: null,
            nextRunAt: null, lastFiredAt: null, createdAt: now, updatedAt: now);
    }

    /// <summary>Creates a cron launchpad: fires an autonomous agent session (prompt + rows from
    /// <paramref name="sourceTable"/>) pointed at <paramref name="chainId"/>.</summary>
    public static JobTrigger CreateLaunchpad(
        Guid projectId, string name, string cronExpression, Guid chainId,
        string? sourceTable, string prompt, DateTimeOffset? nextRunAt, DateTimeOffset now)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        if (chainId == Guid.Empty)
            throw new ArgumentException("ChainId must not be empty.", nameof(chainId));
        ValidateName(name);
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("A launchpad requires a cron expression.", nameof(cronExpression));
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("A launchpad requires a prompt.", nameof(prompt));

        return new JobTrigger(
            Guid.NewGuid(), projectId, jobId: null, name.Trim(), TriggerKind.Launchpad,
            enabled: true, cronExpression: cronExpression.Trim(), eventName: null,
            chainId: chainId, sourceTable: string.IsNullOrWhiteSpace(sourceTable) ? null : sourceTable.Trim(),
            prompt: prompt.Trim(), commandId: null,
            nextRunAt: nextRunAt, lastFiredAt: null, createdAt: now, updatedAt: now);
    }

    public static JobTrigger CreateCommandTrigger(
        Guid projectId, string name, string cronExpression, Guid commandId,
        DateTimeOffset? nextRunAt, DateTimeOffset now)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        if (commandId == Guid.Empty)
            throw new ArgumentException("CommandId must not be empty.", nameof(commandId));
        ValidateName(name);
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("A command trigger requires a cron expression.", nameof(cronExpression));

        return new JobTrigger(
            Guid.NewGuid(), projectId, jobId: null, name.Trim(), TriggerKind.Command,
            enabled: true, cronExpression: cronExpression.Trim(), eventName: null,
            chainId: null, sourceTable: null, prompt: null, commandId: commandId,
            nextRunAt: nextRunAt, lastFiredAt: null, createdAt: now, updatedAt: now);
    }

    /// <summary>Rehydrates from persisted state. Infrastructure only.</summary>
    public static JobTrigger Rehydrate(
        Guid id, Guid projectId, Guid? jobId, string name, TriggerKind kind, bool enabled,
        string? cronExpression, string? eventName, Guid? chainId, string? sourceTable, string? prompt,
        Guid? commandId,
        DateTimeOffset? nextRunAt, DateTimeOffset? lastFiredAt,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(id, projectId, jobId, name, kind, enabled, cronExpression, eventName,
               chainId, sourceTable, prompt, commandId, nextRunAt, lastFiredAt, createdAt, updatedAt);

    // ── Behaviour ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Records that the trigger fired at <paramref name="firedAt"/>, advancing the schedule to
    /// <paramref name="nextRunAt"/> (pass null for event triggers).</summary>
    public void MarkFired(DateTimeOffset firedAt, DateTimeOffset? nextRunAt)
    {
        LastFiredAt = firedAt;
        if (Kind is TriggerKind.Schedule or TriggerKind.Launchpad)
            NextRunAt = nextRunAt;
        UpdatedAt = firedAt;
    }

    /// <summary>Updates a schedule's cron expression and its recomputed next-run time.</summary>
    public void Reschedule(string cronExpression, DateTimeOffset? nextRunAt, DateTimeOffset now)
    {
        if (Kind is not (TriggerKind.Schedule or TriggerKind.Launchpad))
            throw new InvalidOperationException("Only cron-based triggers can be rescheduled.");
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

    /// <summary>Enables the trigger. For schedules, supply the recomputed next-run time.</summary>
    public void Enable(DateTimeOffset? nextRunAt, DateTimeOffset now)
    {
        Enabled = true;
        if (Kind is TriggerKind.Schedule or TriggerKind.Launchpad)
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
