using PlaceContext.Jobs.Infrastructure.Scheduling;

namespace PlaceContext.Jobs.Tests;

/// <summary>Tests the Cronos-backed cron adapter: validation, next-occurrence, and timezone handling.</summary>
public class CronosCronScheduleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);
    private readonly CronosCronSchedule _cron = new();

    [Theory]
    [InlineData("0 0 * * *")]      // daily at midnight (5-field)
    [InlineData("*/5 * * * *")]    // every 5 minutes
    [InlineData("0 0 9 * * *")]    // 6-field with seconds
    public void IsValid_accepts_well_formed_expressions(string expr)
        => Assert.True(_cron.IsValid(expr));

    [Theory]
    [InlineData("")]
    [InlineData("not a cron")]
    [InlineData("99 99 99 99 99")]
    public void IsValid_rejects_malformed_expressions(string expr)
        => Assert.False(_cron.IsValid(expr));

    [Fact]
    public void Next_daily_midnight_utc_returns_following_midnight()
    {
        var next = _cron.Next("0 0 * * *", T0, "UTC");
        Assert.Equal(new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Next_every_five_minutes_advances_strictly()
    {
        var next = _cron.Next("*/5 * * * *", T0, "UTC");
        Assert.Equal(T0.AddMinutes(5), next);
    }

    [Fact]
    public void Next_honours_tenant_timezone()
    {
        // Midnight in New York (UTC-4 in June) is 04:00 UTC the next day.
        var next = _cron.Next("0 0 * * *", T0, "America/New_York");
        Assert.Equal(new DateTimeOffset(2026, 6, 26, 4, 0, 0, TimeSpan.Zero), next!.Value.ToUniversalTime());
    }

    [Fact]
    public void Next_returns_utc_even_for_offset_timezones()
    {
        // Npgsql only writes UTC DateTimeOffsets to timestamptz — a +10:00 answer from Cronos
        // made every schedule scan throw. The port must always answer in UTC.
        var next = _cron.Next("48 11 * * *", T0, "Australia/Brisbane");
        Assert.NotNull(next);
        Assert.Equal(TimeSpan.Zero, next!.Value.Offset);
    }

    [Fact]
    public void Next_returns_null_for_invalid_expression()
        => Assert.Null(_cron.Next("garbage", T0, "UTC"));
}
