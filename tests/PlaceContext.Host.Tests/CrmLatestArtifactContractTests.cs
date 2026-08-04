namespace PlaceContext.Host.Tests;

public sealed class CrmLatestArtifactContractTests
{
    [Fact]
    public void Client_artifacts_show_only_the_newest_record_for_each_title()
    {
        var root = FindRepoRoot();
        var page = File.ReadAllText(
            Path.Combine(root, "src", "PlaceContext.Host", "Components", "Pages", "Crm.razor")
        );
        var viewModel = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "PlaceContext.Host",
                "Components",
                "ViewModels",
                "Crm",
                "CrmViewModel.cs"
            )
        );

        Assert.Contains("Vm.LatestClientArtifacts", page, StringComparison.Ordinal);
        Assert.Contains(
            "public IReadOnlyList<CrmClientArtifactView> LatestClientArtifacts",
            viewModel,
            StringComparison.Ordinal
        );
        Assert.Contains(
            ".GroupBy(item => item.Title.Trim(), StringComparer.OrdinalIgnoreCase)",
            viewModel,
            StringComparison.Ordinal
        );
        Assert.Contains(
            ".OrderByDescending(item => item.CreatedAt).First()",
            viewModel,
            StringComparison.Ordinal
        );
        Assert.Contains("LatestClientArtifacts.Where(item =>", viewModel, StringComparison.Ordinal);
        Assert.Contains(
            "Artifacts <span class=\"tab-count\">@Vm.LatestClientArtifacts.Count</span>",
            page,
            StringComparison.Ordinal
        );
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
