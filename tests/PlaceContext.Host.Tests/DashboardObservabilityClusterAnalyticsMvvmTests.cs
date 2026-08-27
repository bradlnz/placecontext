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
    public void Observability_exposes_job_run_and_trace_details()
    {
        var page = ReadHostSource("Components/Pages/Observability.razor");
        var trace = ReadHostSource("Components/Shared/TraceWaterfall.razor");
        var viewModel = ReadHostSource("Components/ViewModels/ObservabilityViewModel.cs");

        Assert.Contains("class=\"run-detail-grid\"", page, StringComparison.Ordinal);
        Assert.Contains("<TraceWaterfall", page, StringComparison.Ordinal);
        Assert.Contains("Vm.CloseLiveTrace", page, StringComparison.Ordinal);
        Assert.Contains("<summary>Details", trace, StringComparison.Ordinal);
        Assert.Contains("service.GetJobRunAsync(runId)", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void Cluster_formats_join_commands_from_the_navigation_base_uri()
    {
        Assert.Equal(
            "curl -fsSL https://portal.example/join.sh | bash -s -- --portal "
                + "https://portal.example --token token --node-type standard-worker",
            ClusterViewModel.BuildJoinCommand("https://portal.example/", "token")
        );
    }

    [Fact]
    public void Cluster_formats_ai_shard_join_and_runtime_command()
    {
        var command = ClusterViewModel.BuildAiShardJoinCommand(
            "https://portal.example/",
            "token",
            shardIndex: 1,
            totalShards: 3);

        Assert.Contains("--node-type ai-shard", command, StringComparison.Ordinal);
        Assert.Contains("--ai-shard --shard-index 1 --total-shards 3", command, StringComparison.Ordinal);
    }

    [Fact]
    public void Analytics_exposes_analytics_as_a_data_section()
    {
        Assert.Equal(DataSection.Analytics, ProjectAnalyticsViewModel.ActiveSection);
    }

    [Fact]
    public void Entities_catalogue_does_not_render_raw_linked_values()
    {
        var page = ReadHostSource("Components/Pages/DataEntities.razor");

        Assert.DoesNotContain("Linked values", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Vm.LinkGroups", page, StringComparison.Ordinal);
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
