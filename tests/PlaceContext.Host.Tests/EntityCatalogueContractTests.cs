namespace PlaceContext.Host.Tests;

public sealed class EntityCatalogueContractTests
{
    [Fact]
    public void Entities_use_a_non_overflowing_job_style_catalogue()
    {
        var root = FindRepoRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "PlaceContext.Host", "Components", "Pages", "DataEntities.razor"));
        var css = File.ReadAllText(Path.Combine(root, "src", "PlaceContext.Host", "Components", "Pages", "DataEntities.razor.css"));

        Assert.Contains("class=\"dccard entity-suite\"", page, StringComparison.Ordinal);
        Assert.Contains("class=\"entity-list\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"entity-graph\"", page, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 36px minmax(0, 1fr) auto;", css, StringComparison.Ordinal);
        Assert.Contains("min-width: 0;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Auto_linked_records_route_through_the_entity_mapped_to_the_table()
    {
        var root = FindRepoRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "PlaceContext.Host", "Components", "Pages", "EntityBrowse.razor"));

        Assert.Contains("EntityNameForTable(link.TableName)", page, StringComparison.Ordinal);
        Assert.Contains("Vm.AllEntities.FirstOrDefault", page, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "PlaceContext.Host"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
