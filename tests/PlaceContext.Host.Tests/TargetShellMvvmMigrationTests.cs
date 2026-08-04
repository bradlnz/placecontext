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
