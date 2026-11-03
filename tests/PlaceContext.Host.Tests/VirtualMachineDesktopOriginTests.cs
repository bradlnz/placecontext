using PlaceContext.Host.Controllers;
namespace PlaceContext.Host.Tests;

public sealed class VirtualMachineDesktopOriginTests
{
    [Theory]
    [InlineData("https://portal.example", true)]
    [InlineData("http://portal.example", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("https://portal.example.evil.example", false)]
    [InlineData("https://portal.example:444", false)]
    [InlineData(null, false)]
    [InlineData("null", false)]
    public void Console_rejects_cross_origin_and_missing_origins(string? origin, bool expected) =>
        Assert.Equal(expected, VirtualMachineDesktopController.SameOrigin(origin, "https", "portal.example"));
}
