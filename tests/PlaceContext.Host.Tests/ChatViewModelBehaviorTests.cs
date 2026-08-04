using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class ChatViewModelBehaviorTests
{
    [Fact]
    public void Numbered_options_are_only_available_when_two_options_are_present()
    {
        Assert.Empty(ChatViewModel.ParseNumberedOptions("1. Only option"));
        Assert.Equal(
            ["Overview", "Recent runs"],
            ChatViewModel.ParseNumberedOptions("1. Overview\n2) Recent runs")
        );
    }

    [Fact]
    public void Tool_result_formatting_escapes_table_values_in_the_view_model()
    {
        var formatted = ChatViewModel.FormatToolResultPresentation(
            "Name | Status\n---\n<job> | Ready"
        );

        Assert.Contains("&lt;job&gt;", formatted, StringComparison.Ordinal);
        Assert.Contains("class=\"tool-table\"", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_filtering_is_case_insensitive_and_preserves_matching_order()
    {
        var commands = new[]
        {
            new PlaceContext.Application.Dtos.ChatCommandView(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Latest-Runs",
                "Recent jobs",
                "list_job_runs",
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow
            ),
            new PlaceContext.Application.Dtos.ChatCommandView(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Graph",
                "Project graph",
                "render_graph",
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow
            ),
        };

        Assert.Equal(
            "Latest-Runs",
            Assert.Single(ChatViewModel.FilterCommands(commands, "RUNS")).Name
        );
    }

    [Fact]
    public void Chat_catalog_maps_roles_results_and_settings_tabs()
    {
        Assert.Equal(ChatRole.Assistant, ChatPresentationCatalog.ParseRole("assistant"));
        Assert.Equal(ChatResultKind.Graph, ChatPresentationCatalog.ParseResultKind("graph"));
        Assert.Equal(
            ChatSettingsTab.Commands,
            ChatPresentationCatalog.ParseSettingsTab("commands")
        );
    }
}
