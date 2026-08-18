using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

/// <summary>
/// Deterministic <see cref="ICronSchedule"/> for tests: treats any non-empty string as valid and
/// returns <see cref="Now"/> + <see cref="Interval"/> as the next occurrence.
/// </summary>
public sealed class FakeCronSchedule : ICronSchedule
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
    public bool ValidOverride { get; set; } = true;

    public bool IsValid(string cronExpression) => ValidOverride && !string.IsNullOrWhiteSpace(cronExpression);

    public DateTimeOffset? Next(string cronExpression, DateTimeOffset after, string timeZoneId)
        => IsValid(cronExpression) ? after + Interval : null;
}
