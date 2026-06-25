namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// How a <see cref="PlaceContext.Domain.Entities.JobTrigger"/> fires a job run:
/// on a recurring cron <see cref="Schedule"/>, or in reaction to a named <see cref="Event"/>.
/// </summary>
public enum TriggerKind
{
    /// <summary>Fires on a recurring cron schedule.</summary>
    Schedule,

    /// <summary>Fires when an event with a matching name is emitted.</summary>
    Event
}
