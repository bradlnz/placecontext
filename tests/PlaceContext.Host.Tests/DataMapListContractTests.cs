namespace PlaceContext.Host.Tests;

public sealed class DataMapListContractTests
{
    [Fact]
    public void Data_map_is_a_job_list_that_exposes_unmapped_outputs()
    {
        var root = FindRepoRoot();
        var page = File.ReadAllText(
            Path.Combine(root, "src", "PlaceContext.Host", "Components", "Pages", "DataMap.razor")
        );
        var css = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "PlaceContext.Host",
                "Components",
                "Pages",
                "DataMap.razor.css"
            )
        );

        Assert.Contains("class=\"mapping-rows\"", page, StringComparison.Ordinal);
        Assert.Contains("No data mapping", page, StringComparison.Ordinal);
        Assert.Contains(
            "Completed runs do not populate a project table.",
            page,
            StringComparison.Ordinal
        );
        Assert.Contains("MappingsFor(job.Id)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@onpointermove", page, StringComparison.Ordinal);
        Assert.Contains(".mapping-row {", css, StringComparison.Ordinal);
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
