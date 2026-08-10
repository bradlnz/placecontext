namespace PlaceContext.Host.Tests;

public sealed class LoadingPerformanceContractTests
{
    [Fact]
    public void Heavy_visualization_libraries_are_loaded_on_demand()
    {
        var app = ReadHostSource("Components/App.razor");
        var charts = ReadHostSource("wwwroot/pcchart.js");
        var maps = ReadHostSource("wwwroot/pcmap.js");

        Assert.DoesNotContain("<script src=\"vendor/chart.umd.min.js\"", app, StringComparison.Ordinal);
        Assert.DoesNotContain("unpkg.com/leaflet@1.9.4/dist/leaflet.js", app, StringComparison.Ordinal);
        Assert.Contains("function ensureChart()", charts, StringComparison.Ordinal);
        Assert.Contains("function ensureLeaflet()", maps, StringComparison.Ordinal);
    }

    [Fact]
    public void Noncritical_shell_scripts_are_deferred()
    {
        var app = ReadHostSource("Components/App.razor");

        Assert.Contains("<script defer src=\"pcchart.js", app, StringComparison.Ordinal);
        Assert.Contains("<script defer src=\"pcmap.js", app, StringComparison.Ordinal);
        Assert.Contains("media=\"print\" onload=\"this.media='all'\"", app, StringComparison.Ordinal);
    }

    private static string ReadHostSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var host = Path.Combine(directory.FullName, "src", "PlaceContext.Host");
            if (Directory.Exists(host))
                return File.ReadAllText(Path.Combine(host, relativePath));
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
