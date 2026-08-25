using Microsoft.Extensions.Configuration;
using PlaceContext.Infrastructure.Scheduling;

namespace PlaceContext.Infrastructure.Tests;

public sealed class RunStatusWatcherServiceConfigurationTests
{
    [Fact]
    public void Defaults_to_two_second_notification_refresh()
    {
        var configuration = new ConfigurationBuilder().Build();

        var interval = RunStatusWatcherService.ResolveWatchInterval(configuration);

        Assert.Equal(TimeSpan.FromSeconds(2), interval);
    }

    [Fact]
    public void Uses_configured_notification_refresh_interval()
    {
        var configuration = ConfigurationWithInterval("1.25");

        var interval = RunStatusWatcherService.ResolveWatchInterval(configuration);

        Assert.Equal(TimeSpan.FromMilliseconds(1250), interval);
    }

    [Theory]
    [InlineData("0.1", 500)]
    [InlineData("120", 60_000)]
    public void Clamps_unsafe_notification_refresh_intervals(string seconds, int expectedMilliseconds)
    {
        var configuration = ConfigurationWithInterval(seconds);

        var interval = RunStatusWatcherService.ResolveWatchInterval(configuration);

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), interval);
    }

    private static IConfiguration ConfigurationWithInterval(string seconds) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["PlaceContext:RunStatusWatcher:IntervalSeconds"] = seconds,
                }
            )
            .Build();
}
