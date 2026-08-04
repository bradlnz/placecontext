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
        Assert.Contains("aria-expanded=\"@Vm.TocOpen\"", markup);
        Assert.Contains("id=\"wiki-contents\"", markup);
        Assert.Contains("@media (max-width: 767px)", styles);
        Assert.Contains(".toc.open", styles);
    }

    [Fact]
    public void Settings_uses_the_wiki_section_navigation_pattern()
    {
        var markup = ReadHostSource("Components/Layout/SettingsLayout.razor");
        var styles = ReadHostSource("Components/Layout/SettingsLayout.razor.css");

        Assert.Contains("class=\"settings-toggle\"", markup);
        Assert.Contains("aria-controls=\"settings-sections\"", markup);
        Assert.Contains("aria-expanded=\"@Vm.NavigationOpen\"", markup);
        Assert.Contains("id=\"settings-sections\"", markup);
        Assert.Contains("aria-label=\"Settings sections\"", markup);
        Assert.Contains(".settings-nav.open", styles);
        Assert.Contains("@media (max-width: 767px)", styles);
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

    [Fact]
    public void Mobile_data_tabs_scroll_horizontally_without_shrinking()
    {
        var styles = ReadHostSource("Components/Shared/DataTabs.razor.css");

        Assert.Contains("overflow-x: auto", styles);
        Assert.Contains("flex: none", styles);
        Assert.Contains("scrollbar-width: thin", styles);
    }

    [Fact]
    public void Data_tables_page_uses_the_shared_data_navigation()
    {
        var page = ReadHostSource("Components/Pages/ProjectData.razor");

        Assert.Contains("<DataTabs ProjectId=\"ProjectId\" Active=\"@DataSection.Tables\" />", page);
    }

    [Fact]
    public void Data_map_only_presents_job_to_table_mappings()
    {
        var page = ReadHostSource("Components/Pages/DataMap.razor");
        var viewModel = ReadHostSource("Components/ViewModels/DataMap/DataMapViewModel.cs");
        var canvas = ReadHostSource("Components/ViewModels/DataMap/DataMapViewModel.Canvas.cs");
        var editor = ReadHostSource("Components/ViewModels/DataMap/DataMapViewModel.Editor.cs");

        Assert.DoesNotContain("JobChainView", page);
        Assert.DoesNotContain("ConnectChain", page);
        Assert.DoesNotContain("ListJobChainsAsync", viewModel);
        Assert.DoesNotContain("ConnectChain", canvas);
        Assert.DoesNotContain("OpenEditorForChain", editor);
    }

    [Fact]
    public void Project_analytics_matches_entity_analytics_responsive_layout()
    {
        var styles = ReadHostSource("Components/Pages/ProjectAnalytics.razor.css");
        const string breakpoint = "@media (max-width: 950px)";

        Assert.Contains(breakpoint, styles);
        var responsive = styles.Split(breakpoint, 2)[1];
        Assert.Contains(".page { padding: 16px 14px 28px; }", responsive);
        Assert.Contains(".page-head { flex-wrap: wrap", responsive);
        Assert.Contains(".sql-actions { flex-wrap: wrap; }", responsive);
        Assert.Contains(".sql-chart-grid, .table-chart-grid { grid-template-columns: 1fr; }", responsive);
        Assert.Contains(".chart-canvas-box { height: 220px; }", responsive);
    }

    [Fact]
    public void Observability_uses_the_jobs_catalogue_visual_hierarchy()
    {
        var page = ReadHostSource("Components/Pages/Observability.razor");
        var styles = ReadHostSource("Components/Pages/Observability.razor.css");

        Assert.Contains("class=\"summary-strip\"", page);
        Assert.Contains("class=\"dccard run-suite\"", page);
        Assert.Contains("class=\"suite-head\"", page);
        Assert.Contains("width: min(1120px, 100%);", styles);
        Assert.Contains("border-bottom: 1px solid var(--border);", styles);
        Assert.Contains("@media (max-width: 950px)", styles);
    }

    [Fact]
    public void Crm_uses_the_wiki_subpage_navigation_pattern()
    {
        var page = ReadHostSource("Components/Pages/Crm.razor");
        var styles = ReadHostSource("Components/Pages/Crm.razor.css");

        Assert.Contains("class=\"crm-nav-toggle\"", page);
        Assert.Contains("id=\"crm-sections\"", page);
        Assert.Contains("crm-section-nav", page);
        Assert.Contains("class=\"crm-workspace\"", page);
        Assert.Contains(".crm-shell", styles);
        Assert.Contains(".crm-section-nav.open", styles);
        Assert.Contains("@media (max-width: 767px)", styles);
    }

    [Fact]
    public void Crm_directory_uses_an_enterprise_list_to_detail_hierarchy()
    {
        var page = ReadHostSource("Components/Pages/Crm.razor");
        var styles = ReadHostSource("Components/Pages/Crm.razor.css");

        Assert.Contains("class=\"client-table\"", page);
        Assert.Contains("class=\"client-table-head\"", page);
        Assert.Contains("client-row", page);
        Assert.Contains("client-row-identity", page);
        Assert.Contains("client-row-contact", page);
        Assert.Contains("client-row-stage", page);
        Assert.Contains(".client-table-head", styles);
        Assert.Contains("grid-template-columns: minmax(210px, 1.35fr) minmax(190px, 1fr) minmax(150px, .65fr) 44px;", styles);
        Assert.Contains(".client-row-field-label", styles);
    }

    private static string ReadHostSource(string relativePath)
    {
        var hostRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/PlaceContext.Host"));

        return File.ReadAllText(Path.Combine(hostRoot, relativePath));
    }
}