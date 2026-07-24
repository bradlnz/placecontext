namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// How a <see cref="PlaceContext.Domain.Entities.JobTrigger"/> fires: on a recurring cron
/// <see cref="Schedule"/>, in reaction to a named <see cref="Event"/>, or as a cron
/// <see cref="Launchpad"/> that starts an autonomous agent session.
/// </summary>
public enum TriggerKind
{
    /// <summary>Fires on a recurring cron schedule.</summary>
    Schedule,

    /// <summary>Fires when an event with a matching name is emitted.</summary>
    Event,

    /// <summary>
    /// Fires on a recurring cron schedule, but instead of enqueueing a job run it launches an
    /// agent session (prompt + fetched table rows) that autonomously runs job chains.
    /// </summary>
    Launchpad
}
