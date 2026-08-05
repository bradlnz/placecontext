namespace PlaceContext.Host.Tests;

public sealed class JobsSidePaneResponsiveContractTests
{
    [Fact]
    public void Job_side_pane_header_actions_wrap_on_mobile()
    {
        var page = ReadHostSource("Components/Pages/Jobs.razor");
        var styles = ReadHostSource("Components/Pages/Jobs.razor.css");
        const string breakpoint = "@media (max-width: 700px)";

        Assert.Contains("dcslide-head job-slide-head", page);
        Assert.Contains("job-slide-run", page);
        Assert.Contains(breakpoint, styles);

        var responsive = styles.Split(breakpoint, 2)[1];
        Assert.Contains(".job-slide-head", responsive);
        Assert.Contains("flex-wrap: wrap", responsive);
        Assert.Contains(".job-slide-run", responsive);
        Assert.Contains("width: 100%", responsive);
        Assert.Contains("min-height: 44px", responsive);
    }

    private static string ReadHostSource(string relativePath)
    {
        var hostRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../src/PlaceContext.Host")
        );

        return File.ReadAllText(Path.Combine(hostRoot, relativePath));
    }
}
