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
        Assert.DoesNotContain("chart.umd", app + charts, StringComparison.Ordinal);
        Assert.Contains("createElementNS", charts, StringComparison.Ordinal);
        Assert.Contains("function ensureLeaflet()", maps, StringComparison.Ordinal);
    }

    [Fact]
    public void Noncritical_shell_scripts_are_deferred()
    {
        var app = ReadHostSource("Components/App.razor");

        Assert.Contains("<script defer src=\"@Assets[\"pcchart.js", app, StringComparison.Ordinal);
        Assert.Contains("<script defer src=\"@Assets[\"pcmodules.js", app, StringComparison.Ordinal);
        Assert.Contains("media=\"print\" onload=\"this.media='all'\"", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Interop_modules_are_registered_before_blazor_starts()
    {
        var app = ReadHostSource("Components/App.razor");
        var charts = app.IndexOf("<script defer src=\"@Assets[\"pcchart.js", StringComparison.Ordinal);
        var blazor = app.IndexOf(
            "<script defer src=\"_framework/blazor.web.js\"",
            StringComparison.Ordinal
        );

        Assert.True(charts >= 0, "The chart interop module must be loaded.");
        var modules = app.IndexOf("<script defer src=\"@Assets[\"pcmodules.js", StringComparison.Ordinal);
        Assert.True(modules >= 0 && blazor > modules, "Lazy interop must be registered before Blazor starts.");
        Assert.True(blazor > charts, "Blazor must start after the chart interop module.");
    }

    [Fact]
    public void Chart_renderer_tracks_replaced_containers_and_resizing()
    {
        var charts = ReadHostSource("wwwroot/pcchart.js");

        Assert.Contains("state.element !== document.getElementById(id)", charts, StringComparison.Ordinal);
        Assert.Contains("new ResizeObserver", charts, StringComparison.Ordinal);
        Assert.Contains("new MutationObserver(schedule)", charts, StringComparison.Ordinal);
    }

    private static string ReadHostSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var host = Path.Combine(directory.FullName, "src", "PlaceContext.Host");
            if (Directory.Exists(host))
            {
                return File.ReadAllText(Path.Combine(host, relativePath));
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
