namespace PlaceContext.Host.Tests;

using PlaceContext.Host.Components.ViewModels;

public sealed class DataTablesListContractTests
{
    [Fact]
    public void Data_tables_use_the_job_catalogue_list_pattern()
    {
        var root = FindRepoRoot();
        var page = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "PlaceContext.Host",
                "Components",
                "Pages",
                "ProjectData.razor"
            )
        );
        var css = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "PlaceContext.Host",
                "Components",
                "Pages",
                "ProjectData.razor.css"
            )
        );

        Assert.Contains("class=\"dccard table-suite\"", page, StringComparison.Ordinal);
        Assert.Contains("class=\"table-rows\"", page, StringComparison.Ordinal);
        Assert.Contains("class=\"table-row\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"table-tile\"", page, StringComparison.Ordinal);
        Assert.Contains(".table-row {", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_a_table_runs_its_query_and_json_uses_a_full_screen_side_viewer()
    {
        var root = FindRepoRoot();
        var page = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "PlaceContext.Host",
                "Components",
                "Pages",
                "ProjectData.razor"
            )
        );
        var css = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "PlaceContext.Host",
                "Components",
                "Pages",
                "ProjectData.razor.css"
            )
        );
        var viewModel = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "PlaceContext.Host",
                "Components",
                "ViewModels",
                "ProjectData",
                "ProjectDataViewModel.cs"
            )
        );

        Assert.Contains("OpenTableModalAsync", page, StringComparison.Ordinal);
        Assert.Contains(
            "await RunModalAsync(() => Task.FromResult(ModalSql))",
            viewModel,
            StringComparison.Ordinal
        );
        Assert.Contains("class=\"json-view-button\"", page, StringComparison.Ordinal);
        Assert.Contains("class=\"json-side-pane\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"dccard sql-workspace\"", page, StringComparison.Ordinal);
        Assert.Contains(
            "var columnName = Vm.ModalResult.Columns[cellIndex];",
            page,
            StringComparison.Ordinal
        );
        Assert.Contains("OpenJsonViewer(columnName, cellValue!)", page, StringComparison.Ordinal);
        Assert.Contains("padding: 0;", css, StringComparison.Ordinal);
        Assert.Contains("border-radius: 0;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Indexes_tab_explains_missing_opensearch_and_links_to_setup()
    {
        var root = FindRepoRoot();
        var page = File.ReadAllText(
            Path.Combine(root, "src", "PlaceContext.Host", "Components", "Pages", "ProjectData.razor")
        );
        var connections = File.ReadAllText(
            Path.Combine(root, "src", "PlaceContext.Host", "Components", "Pages", "ConnectionsSettings.razor")
        );

        Assert.Contains("Vm.OpenSearchSetupRequired", page, StringComparison.Ordinal);
        Assert.Contains("OpenSearch isn’t set up", page, StringComparison.Ordinal);
        Assert.Contains("Set up OpenSearch", page, StringComparison.Ordinal);
        Assert.Contains("href=\"@Vm.OpenSearchSetupUrl\"", page, StringComparison.Ordinal);
        Assert.Contains("SupplyParameterFromQuery(Name = \"project\")", connections, StringComparison.Ordinal);
        Assert.Contains("id=\"search-index\"", connections, StringComparison.Ordinal);
    }

    [Fact]
    public void Indexes_tab_only_treats_the_not_configured_error_as_setup_required()
    {
        Assert.True(
            ProjectDataViewModel.IsOpenSearchConfigurationMissing(
                new InvalidOperationException("OpenSearch is not configured. Add OPENSEARCH_URL to this project's Vault.")
            )
        );
        Assert.False(
            ProjectDataViewModel.IsOpenSearchConfigurationMissing(
                new HttpRequestException("Connection refused")
            )
        );
    }

    [Fact]
    public void Indexes_tab_keeps_a_load_failure_visible_instead_of_returning_to_a_skeleton()
    {
        var root = FindRepoRoot();
        var page = File.ReadAllText(
            Path.Combine(root, "src", "PlaceContext.Host", "Components", "Pages", "ProjectData.razor")
        );
        var viewModel = File.ReadAllText(
            Path.Combine(root, "src", "PlaceContext.Host", "Components", "ViewModels", "ProjectData", "ProjectDataViewModel.Studio.cs")
        );

        Assert.True(
            page.IndexOf("@if (Vm.OpenSearchSetupRequired)", StringComparison.Ordinal)
            < page.IndexOf("else if (!Vm.IndicesReady)", StringComparison.Ordinal)
        );
        Assert.True(
            page.IndexOf("else if (Vm.IndicesError is not null)", StringComparison.Ordinal)
            < page.IndexOf("else if (!Vm.IndicesReady)", StringComparison.Ordinal)
        );
        Assert.Contains("if (!force && IndicesReady)", viewModel, StringComparison.Ordinal);
        Assert.Contains("_ = LoadIndicesAsync();", viewModel, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "PlaceContext.Host")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
