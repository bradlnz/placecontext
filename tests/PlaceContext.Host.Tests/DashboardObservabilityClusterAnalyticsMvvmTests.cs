using PlaceContext.Host.Components.Models;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class DashboardObservabilityClusterAnalyticsMvvmTests
{
    [Theory]
    [InlineData("Dashboard", "DashboardViewModel")]
    [InlineData("Observability", "ObservabilityViewModel")]
    [InlineData("ProjectAnalytics", "ProjectAnalyticsViewModel")]
    [InlineData("Cluster", "ClusterViewModel")]
    public void Focused_pages_use_only_their_view_model(string pageName, string viewModelName)
    {
        var page = ReadHostSource($"Components/Pages/{pageName}.razor");

        Assert.Contains($"@inject {viewModelName} Vm", page, StringComparison.Ordinal);
        Assert.Contains("Vm.Attach", page, StringComparison.Ordinal);
        Assert.Contains("@implements IDisposable", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject IPlaceContextService", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject NavigationManager", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject IJSRuntime", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject OperationCenter", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Analytics_uses_the_typed_data_section()
    {
        var page = ReadHostSource("Components/Pages/ProjectAnalytics.razor");

        Assert.Contains("Active=\"@DataSection.Analytics\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Active=\"analytics\"", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Succeeded", "var(--good)")]
    [InlineData("Failed", "var(--bad)")]
    [InlineData("Running", "var(--good)")]
    public void Dashboard_status_colors_are_view_model_decisions(string status, string expected)
    {
        Assert.Equal(expected, DashboardViewModel.StatusColor(status));
    }

    [Theory]
    [InlineData(0, "0 ms")]
    [InlineData(999, "999 ms")]
    [InlineData(1000, "1 s")]
    public void Observability_formats_trace_durations_in_the_view_model(
        double milliseconds,
        string expected
    )
    {
        Assert.Equal(expected, ObservabilityViewModel.FormatMilliseconds(milliseconds));
    }

    [Fact]
    public void Cluster_formats_join_commands_from_the_navigation_base_uri()
    {
        Assert.Equal(
            "curl -fsSL https://portal.example/join.sh | bash -s -- --portal "
                + "https://portal.example --token token",
            ClusterViewModel.BuildJoinCommand("https://portal.example/", "token")
        );
    }

    [Fact]
    public void Analytics_exposes_analytics_as_a_data_section()
    {
        Assert.Equal(DataSection.Analytics, ProjectAnalyticsViewModel.ActiveSection);
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
