using PlaceContext.Host;

namespace PlaceContext.Host.Tests;

public sealed class LocalityTimeZonesTests
{
    [Fact]
    public void Dropdown_options_are_sorted_valid_iana_timezones()
    {
        var options = LocalityTimeZones.All;

        Assert.NotEmpty(options);
        Assert.Equal(options.Order(StringComparer.Ordinal), options);
        Assert.Contains("UTC", options);
        Assert.Contains("Australia/Brisbane", options);
        Assert.All(options, id => Assert.Equal(id, TimeZoneInfo.FindSystemTimeZoneById(id).Id));
    }
}
