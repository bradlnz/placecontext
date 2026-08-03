namespace PlaceContext.Host.Tests;

public sealed class CrmDesktopTabsContractTests
{
    [Fact]
    public void Desktop_crm_uses_full_width_section_tabs()
    {
        var root = FindRepoRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "PlaceContext.Host", "Components", "Pages", "Crm.razor"));
        var css = File.ReadAllText(Path.Combine(root, "src", "PlaceContext.Host", "Components", "Pages", "Crm.razor.css"));

        Assert.Contains("aria-label=\"CRM sections\" role=\"tablist\"", page, StringComparison.Ordinal);
        Assert.Contains("role=\"tab\" aria-selected", page, StringComparison.Ordinal);
        Assert.Contains(".crm-page {\n    width: 100%;\n    max-width: none;", css, StringComparison.Ordinal);
        Assert.Contains(".crm-shell {\n    display: block;", css, StringComparison.Ordinal);
        Assert.Contains(".crm-section-nav {\n    width: 100%;\n    display: flex;", css, StringComparison.Ordinal);
        Assert.Contains("border-bottom-color: var(--brand);", css, StringComparison.Ordinal);
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
