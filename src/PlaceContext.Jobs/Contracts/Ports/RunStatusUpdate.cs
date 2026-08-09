namespace PlaceContext.Application.Ports;

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
