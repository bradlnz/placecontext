using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class ArtifactsOpenSearchMvvmTests
{
    [Theory]
    [InlineData("Artifacts.razor", "ArtifactsViewModel")]
    [InlineData("OpenSearchData.razor", "OpenSearchDataViewModel")]
    public void Target_page_is_a_thin_view_over_a_focused_view_model(
        string pageName,
        string viewModelName
    )
    {
        var page = ReadHostSource(pageName);

        Assert.Contains($"@inject {viewModelName} Vm", page, StringComparison.Ordinal);
        Assert.Contains("Vm.Attach", page, StringComparison.Ordinal);
        Assert.Contains("Vm.Detach", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject IPlaceContextService", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject IJSRuntime", page, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task", page, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool", page, StringComparison.Ordinal);
        Assert.DoesNotContain("private string", page, StringComparison.Ordinal);
    }

    [Fact]
    public void View_models_are_registered_as_page_view_models()
    {
        Assert.True(
            typeof(PlaceContext.Host.Components.ViewModels.PageViewModel).IsAssignableFrom(
                typeof(PlaceContext.Host.Components.ViewModels.ArtifactsViewModel)
            )
        );
        Assert.True(
            typeof(PlaceContext.Host.Components.ViewModels.PageViewModel).IsAssignableFrom(
                typeof(PlaceContext.Host.Components.ViewModels.OpenSearchDataViewModel)
            )
        );
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1048576, "1 MB")]
    public void Artifacts_formatting_is_owned_by_the_view_model(long bytes, string expected)
    {
        Assert.Equal(
            expected,
            PlaceContext.Host.Components.ViewModels.ArtifactsViewModel.FormatBytes(bytes)
        );
    }

    [Fact]
    public void OpenSearch_request_helpers_keep_blank_values_out_of_the_query()
    {
        Assert.Null(
            PlaceContext.Host.Components.ViewModels.OpenSearchDataViewModel.NullIfBlank("  ")
        );
        Assert.Equal(
            "status",
            PlaceContext.Host.Components.ViewModels.OpenSearchDataViewModel.NullIfBlank(" status ")
        );
        Assert.Equal(
            "short",
            PlaceContext.Host.Components.ViewModels.OpenSearchDataViewModel.ShortValue("short")
        );
    }

    [Fact]
    public void OpenSearch_catalog_maps_metric_and_bucket_modes()
    {
        Assert.Equal(
            OpenSearchMetricMode.Average,
            OpenSearchPresentationCatalog.ParseMetric("avg")
        );
        Assert.Equal(
            OpenSearchMetricMode.Count,
            OpenSearchPresentationCatalog.ParseMetric("unknown")
        );
        Assert.Equal(
            OpenSearchBucketMode.DateHistogram,
            OpenSearchPresentationCatalog.ParseBucket("date_histogram")
        );
        Assert.Equal("terms", OpenSearchPresentationCatalog.BucketKey(OpenSearchBucketMode.Terms));
    }

    private static string ReadHostSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var host = Path.Combine(directory.FullName, "src", "PlaceContext.Host");
            if (Directory.Exists(host))
            {
                return File.ReadAllText(Path.Combine(host, "Components", "Pages", relativePath));
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
