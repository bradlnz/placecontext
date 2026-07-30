namespace PlaceContext.Application.Ports;

/// <summary>Where a run is in its lifecycle, as surfaced to notification consumers.</summary>
public enum RunOutcome { Running, Succeeded, Partial, Failed, Cancelled }

/// <summary>
/// One observed run-status change. <see cref="Key"/> is a stable correlation key
/// (<c>job-run:{id:N}</c> / <c>chain-run:{id:N}</c>) so repeated updates for the same run
/// converge on one notification entry — including an entry a caller created up front.
/// </summary>
public sealed record RunStatusUpdate(
    string Key,
    Guid ProjectId,
    string Title,
    RunOutcome Outcome,
    string? Detail,
    string? Link,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>
/// Push-side port for the run-status watcher: upserts a run's current status into whatever
/// surfaces notifications (the portal's operation ledger). Updates reflect persisted state, so
/// implementations treat them as authoritative over caller-side progress marks.
/// </summary>
public interface IRunStatusNotifier
{
    void Sync(RunStatusUpdate update);
}
