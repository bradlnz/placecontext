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

    [Fact]
    public void Mobile_modals_use_the_full_viewport_with_accessible_close_targets()
    {
        var styles = ReadHostSource("Components/App.razor");
        const string mobileBreakpoint = "@@media (max-width: 700px)";

        Assert.Contains(mobileBreakpoint, styles);
        var mobileStyles = styles.Split(mobileBreakpoint, 2)[1];
        Assert.Contains("width:100vw", mobileStyles);
        Assert.Contains("height:100dvh", mobileStyles);
        Assert.Contains("border-radius:0", mobileStyles);
        Assert.Contains("min-width:44px", mobileStyles);
        Assert.Contains("min-height:44px", mobileStyles);
    }

    [Fact]
    public void Mobile_focus_layers_lock_background_scrolling()
    {
        var styles = ReadHostSource("Components/App.razor");
        var focusLayerRule = styles.Split("html.pc-focus-layer-open", 2)[1].Split('}', 2)[0];

        Assert.Contains("overflow:hidden", focusLayerRule);
    }

    private static string ReadHostSource(string relativePath)
    {
        var hostRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/PlaceContext.Host"));

        return File.ReadAllText(Path.Combine(hostRoot, relativePath));
    }
}
