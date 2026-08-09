namespace PlaceContext.Application.Ports;

/// <summary>
/// Computes fire times from a cron expression. Implemented in Infrastructure (Cronos) so the Domain
/// and Application stay free of any cron-parsing dependency.
/// </summary>
public interface ICronSchedule
{
    /// <summary>True when <paramref name="cronExpression"/> is a parseable cron expression.</summary>
    bool IsValid(string cronExpression);

    /// <summary>
    /// The next fire time strictly after <paramref name="after"/>, evaluated in
    /// <paramref name="timeZoneId"/> (IANA, e.g. "America/New_York"). Returns null when the expression
    /// is invalid or never fires again.
    /// </summary>
    DateTimeOffset? Next(string cronExpression, DateTimeOffset after, string timeZoneId);
}
