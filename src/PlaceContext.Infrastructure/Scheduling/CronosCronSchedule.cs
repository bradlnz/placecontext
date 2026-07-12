using Cronos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>
/// <see cref="ICronSchedule"/> backed by Cronos. Supports standard 5-field cron and 6-field (with
/// seconds) expressions, evaluated in the tenant's IANA time zone (DST-aware). Stays in Infrastructure
/// so the cron-parsing dependency never leaks into Domain/Application.
/// </summary>
public sealed class CronosCronSchedule : ICronSchedule
{
    public bool IsValid(string cronExpression) => TryParse(cronExpression, out _);

    public DateTimeOffset? Next(string cronExpression, DateTimeOffset after, string timeZoneId)
    {
        if (!TryParse(cronExpression, out var expr)) return null;
        var tz = ResolveZone(timeZoneId);
        // Normalize to UTC: Cronos answers in the zone's own offset (e.g. +10:00), and Npgsql
        // refuses to write any non-UTC DateTimeOffset to timestamptz — a zoned value here made
        // every scan tick throw and silently stopped ALL schedules for the tenant.
        return expr!.GetNextOccurrence(after, tz)?.ToUniversalTime();
    }

    private static bool TryParse(string cronExpression, out CronExpression? expr)
    {
        expr = null;
        if (string.IsNullOrWhiteSpace(cronExpression)) return false;
        try
        {
            // 6 whitespace-separated fields → the expression includes a seconds field.
            var fields = cronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var format = fields.Length >= 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
            expr = CronExpression.Parse(cronExpression.Trim(), format);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }

    private static TimeZoneInfo ResolveZone(string timeZoneId)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch { return TimeZoneInfo.Utc; }
    }
}
