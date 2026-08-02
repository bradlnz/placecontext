namespace PlaceContext.Host.Tests;

public sealed class ResponsiveShellContractTests
{
    [Fact]
    public void Viewport_allows_browser_zoom()
    {
        var source = ReadHostSource("Components/App.razor");

        Assert.Contains("name=\"viewport\"", source);
        Assert.Contains("width=device-width, initial-scale=1", source);
        Assert.DoesNotContain("maximum-scale", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user-scalable", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wiki_exposes_an_accessible_mobile_documentation_drawer()
    {
        var markup = ReadHostSource("Components/Pages/Wiki.razor");
        var styles = ReadHostSource("Components/Pages/Wiki.razor.css");

        Assert.Contains("class=\"toc-toggle\"", markup);
        Assert.Contains("aria-controls=\"wiki-contents\"", markup);
        Assert.Contains("aria-expanded=\"@_tocOpen\"", markup);
        Assert.Contains("id=\"wiki-contents\"", markup);
        Assert.Contains("@media (max-width: 767px)", styles);
        Assert.Contains(".toc.open", styles);
    }

    [Fact]
    public void Crm_client_notes_wrap_unbroken_content()
    {
        var styles = ReadHostSource("Components/Pages/Crm.razor.css");
        var clientNotesRule = styles.Split(".client-notes", 2)[1].Split('}', 2)[0];

        Assert.Contains("overflow-wrap: anywhere", clientNotesRule);
    }

    private static string ReadHostSource(string relativePath)
    {
        var hostRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/PlaceContext.Host"));

        return File.ReadAllText(Path.Combine(hostRoot, relativePath));
    }
}
