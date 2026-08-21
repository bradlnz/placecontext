using System.Text.RegularExpressions;
using Microsoft.JSInterop;
using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class TargetShellMvvmMigrationTests
{
    private static readonly string[] TargetViews =
    [
        "Components/Layout/MainLayout.razor",
        "Components/Layout/SettingsLayout.razor",
        "Components/Shared/GraphCanvas.razor",
        "Components/Shared/ParamInput.razor",
    ];

    [Fact]
    public void Target_views_inject_only_their_view_model_and_keep_component_wiring()
    {
        foreach (var relativePath in TargetViews)
        {
            var source = ReadHostSource(relativePath);
            var injections = Regex
                .Matches(source, "^@inject\\s+(?<type>\\S+)", RegexOptions.Multiline)
                .Select(match => match.Groups["type"].Value)
                .ToArray();

            Assert.Single(injections);
            Assert.EndsWith("ViewModel", injections[0], StringComparison.Ordinal);
            Assert.Contains("@code", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Target_views_do_not_own_state_or_service_operations()
    {
        foreach (var relativePath in TargetViews)
        {
            var source = ReadHostSource(relativePath);

            Assert.DoesNotContain("@inject I", source, StringComparison.Ordinal);
            Assert.DoesNotContain("@inject PortalUiState", source, StringComparison.Ordinal);
            Assert.DoesNotContain("@inject NavigationManager", source, StringComparison.Ordinal);
            Assert.DoesNotContain("private async Task", source, StringComparison.Ordinal);
            Assert.DoesNotContain("private IReadOnlyList", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void View_models_expose_behavior_without_blazor_component_dependencies()
    {
        foreach (var name in new[] { "MainLayout", "SettingsLayout", "GraphCanvas", "ParamInput" })
        {
            var source = ReadHostSource($"Components/ViewModels/{name}ViewModel.cs");

            Assert.Contains(": PageViewModel", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ComponentBase", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Graph_canvas_search_is_case_insensitive_and_prioritizes_label_matches()
    {
        var graph = new GraphVizView(
            Guid.NewGuid(),
            2,
            0,
            [
                new GraphNodeView("content", "Alpha", 10, false, "alpha note", "human"),
                new GraphNodeView("label", "Alpha record", 1, false, null, "human"),
                new GraphNodeView("other", "Unrelated", 99, false, "alpha", "human"),
            ],
            []
        );
        var vm = new GraphCanvasViewModel(new NoOpJsRuntime());

        vm.SetParameters(graph, 340, null, true, false);
        vm.SearchNodes("ALPHA");

        Assert.Equal(["content", "label", "other"], vm.Matches.Select(node => node.Id));
    }

    [Fact]
    public async Task Graph_canvas_keeps_multiple_detail_panels_open_until_each_is_closed()
    {
        var graph = new GraphVizView(
            Guid.NewGuid(),
            2,
            1,
            [
                new GraphNodeView("run:one", "Run one", 1, false, null, "JobRun"),
                new GraphNodeView("artifact:two", "report.pdf", 1, false, null, "Artifact"),
            ],
            [new GraphLinkView("run:one", "artifact:two", "Extracted")]
        );
        var vm = new GraphCanvasViewModel(new NoOpJsRuntime());
        vm.SetParameters(graph, 340, null, true, false);

        await vm.OnNodeClick("run:one");
        await vm.OnNodeClick("artifact:two");

        Assert.Equal(["run:one", "artifact:two"], vm.OpenPanels.Select(node => node.Id));

        await vm.ClosePanelAsync("run:one");

        Assert.Equal(["artifact:two"], vm.OpenPanels.Select(node => node.Id));
    }

    [Fact]
    public async Task Graph_canvas_moves_an_open_detail_panel_from_its_drag_handle()
    {
        var graph = new GraphVizView(
            Guid.NewGuid(),
            1,
            0,
            [new GraphNodeView("node:one", "Node one", 0, false, null, "Entity")],
            []
        );
        var vm = new GraphCanvasViewModel(new NoOpJsRuntime());
        vm.SetParameters(graph, 340, null, true, false);
        await vm.OnNodeClick("node:one");

        vm.StartPanelDrag("node:one", 100, 100);
        vm.MovePanel(140, 125);

        Assert.True(vm.IsPanelDragging);
        Assert.Contains("top:39px; left:54px", vm.PanelStyle("node:one"), StringComparison.Ordinal);

        vm.StopPanelDrag();
        Assert.False(vm.IsPanelDragging);
    }

    [Fact]
    public async Task Graph_canvas_single_panel_mode_replaces_the_open_detail_panel()
    {
        var graph = new GraphVizView(
            Guid.NewGuid(),
            2,
            0,
            [
                new GraphNodeView("node:one", "Node one", 0, false, null, "Entity"),
                new GraphNodeView("node:two", "Node two", 0, false, null, "Entity"),
            ],
            []
        );
        var vm = new GraphCanvasViewModel(new NoOpJsRuntime());
        vm.SetParameters(graph, 340, null, true, false, singlePanel: true);

        await vm.OnNodeClick("node:one");
        await vm.OnNodeClick("node:two");

        Assert.Equal(["node:two"], vm.OpenPanels.Select(node => node.Id));
    }

    [Fact]
    public void Graph_canvas_renders_draggable_run_and_artifact_windows_with_close_controls()
    {
        var source = ReadHostSource("Components/Shared/GraphCanvas.razor");

        Assert.Contains("foreach (var sel in Vm.OpenPanels)", source, StringComparison.Ordinal);
        Assert.Contains("class=\"panel-window-handle\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Close panel\"", source, StringComparison.Ordinal);
        Assert.Contains("Vm.RunDetailsFor(sel)", source, StringComparison.Ordinal);
        Assert.Contains("Vm.IsPdf(artifact)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_graph_renders_an_accessible_draggable_split_between_the_node_list_and_canvas()
    {
        var source = ReadHostSource("Components/Pages/DataGraph.razor");
        var viewModel = ReadHostSource("Components/ViewModels/DataGraphViewModel.cs");
        var graphStyles = ReadHostSource("Components/Shared/GraphCanvas.razor.css");

        Assert.Contains("id=\"data-graph-splitter\"", source, StringComparison.Ordinal);
        Assert.Contains("role=\"separator\"", source, StringComparison.Ordinal);
        Assert.Contains("pcgraph.splitter", viewModel, StringComparison.Ordinal);
        Assert.Contains("DockDetails=\"true\"", source, StringComparison.Ordinal);
        Assert.Contains("SinglePanel=\"true\"", source, StringComparison.Ordinal);
        Assert.Contains(
            ".docked-detail-shell.docked-panel-open {\n    display: grid;",
            graphStyles,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Default_workspace_includes_a_job_that_returns_a_named_pdf_file()
    {
        var source = ReadHostSource("Startup/DefaultWorkspaceBootstrap.cs");

        Assert.Contains("\"Generate PDF report\"", source, StringComparison.Ordinal);
        Assert.Contains("JobReturnType.Pdf", source, StringComparison.Ordinal);
        Assert.Contains("returnFileName: \"workspace-report.pdf\"", source, StringComparison.Ordinal);
        Assert.Contains("Path(\"/out\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Param_input_view_model_preserves_file_marker_display_and_accept_catalog()
    {
        var vm = new ParamInputViewModel(null!, null!);
        var parameter = new JobParameterDto(
            "attachment",
            "Attachment",
            true,
            "file",
            ["image/png"]
        );
        var marker = """{"$file":{"filename":"diagram.png"}}""";

        vm.SetParameters(parameter, Guid.NewGuid(), marker);

        Assert.Equal("image/png", vm.Accept);
        Assert.Equal("diagram.png", vm.SelectedFileName);
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

    private sealed class NoOpJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => ValueTask.FromResult(default(TValue)!);
    }
}
