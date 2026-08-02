namespace PlaceContext.Host.Tests;

public sealed class ResponsiveShellContractTests
{
    [Fact]
    public void Viewport_allows_browser_zoom()
    {
        var source = ReadHostSource("Components/App.razor");

        Assert.Contains("name=\"viewport\"", source);
        Assert.Contains("width=device-width, initial-scale=1", source);
        Assert.DoesNotContain("maximum-scale", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user-scalable", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadHostSource(string relativePath)
    {
        var hostRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/PlaceContext.Host"));

        return File.ReadAllText(Path.Combine(hostRoot, relativePath));
    }
}
