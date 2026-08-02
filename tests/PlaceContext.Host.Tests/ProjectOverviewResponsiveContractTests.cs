namespace PlaceContext.Host.Tests;

public sealed class ProjectOverviewResponsiveContractTests
{
    [Fact]
    public void Project_overview_contains_long_content_at_phone_widths()
    {
        var styles = ReadHostSource("Components/Pages/Overview.razor.css");

        Assert.Contains("minmax(min(100%, 380px), 1fr)", styles);
        Assert.Contains("@media (max-width: 700px)", styles);
        Assert.Contains("overflow-wrap: anywhere", styles);
        Assert.Contains("min-width: 0", styles);
    }

    private static string ReadHostSource(string relativePath)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        return File.ReadAllText(Path.Combine(repositoryRoot, "src/PlaceContext.Host", relativePath));
    }
}
