using PlaceContext.Desktop.Services;

namespace PlaceContext.Desktop.Tests;

public sealed class EndpointAddressTests
{
    [Theory]
    [InlineData("localhost:7700", "http://localhost:7700/")]
    [InlineData("placecontext.lan", "http://placecontext.lan/")]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("https://example.com/base?ignored=true#fragment", "https://example.com/base/")]
    public void Parse_normalizes_supported_endpoints(string value, string expected)
    {
        Assert.Equal(expected, EndpointAddress.Parse(value).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://example.com")]
    [InlineData("https://user:password@example.com")]
    public void Parse_rejects_invalid_endpoints(string value)
    {
        Assert.Throws<ArgumentException>(() => EndpointAddress.Parse(value));
    }
}
