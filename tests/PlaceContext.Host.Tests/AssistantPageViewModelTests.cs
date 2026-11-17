using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class AssistantPageViewModelTests
{
    [Theory]
    [InlineData("code")]
    [InlineData("data")]
    [InlineData("agents")]
    [InlineData("observability")]
    [InlineData("infrastructure")]
    public async Task Modal_context_overrides_the_page_and_restores_it_on_close(string kind)
    {
        var vm = new AssistantPageViewModel(new PortalUiState());
        vm.Navigate(Project + "workflows/22222222-2222-2222-2222-222222222222/canvas");
        var page = new object();
        var modal = new object();
        vm.Register(page, () => Task.FromResult("workflow"), null);
        var version = vm.Version;
        Func<string, Task> apply = _ => Task.CompletedTask;
        vm.Register(modal, () => Task.FromResult("current modal draft"), apply, kind: kind);
        Assert.Equal(kind, vm.Kind);
        Assert.True(vm.Version > version);
        version = vm.Version;
        vm.Register(page, () => Task.FromResult("updated workflow"), null);
        Assert.Equal(version, vm.Version);
        Assert.Equal(kind, vm.Kind);
        Assert.Same(apply, vm.Apply);
        using var data = System.Text.Json.JsonDocument.Parse(await vm.ReadAsync());
        Assert.Equal(kind, data.RootElement.GetProperty("agent").GetString());
        Assert.Equal("current modal draft", data.RootElement.GetProperty("context").GetString());
        vm.Unregister(modal);
        Assert.Equal("workflow", vm.Kind);
        Assert.Null(vm.Apply);
        Assert.Contains("updated workflow", await vm.ReadAsync());
        Assert.True(vm.Version > version);
    }

    [Fact]
    public async Task Modal_close_rejects_late_reads_and_navigation_discards_old_modal_contexts()
    {
        var vm = new AssistantPageViewModel(new PortalUiState());
        vm.Navigate(Project + "workflows");
        var page = new object(); var modal = new object();
        vm.Register(page, () => Task.FromResult("catalogue"), null);
        var pending = new TaskCompletionSource<string>();
        vm.Register(modal, () => pending.Task, _ => Task.CompletedTask, kind: "code");
        var read = vm.ReadAsync();
        vm.Unregister(modal);
        pending.SetResult("stale code");
        await Assert.ThrowsAsync<InvalidOperationException>(() => read);
        Assert.Equal("workflow-manager", vm.Kind);
        vm.Register(modal, () => Task.FromResult("old code"), null, kind: "code");
        vm.Navigate(Project + "data");
        vm.Register(new object(), () => Task.FromResult("new schema"), null);
        var version = vm.Version;
        vm.Unregister(page); vm.Unregister(modal);
        Assert.Equal(version, vm.Version);
        Assert.Equal("data", vm.Kind);
        Assert.DoesNotContain("old code", await vm.ReadAsync());
    }

    [Fact]
    public void Changing_the_specialist_resets_the_conversation_but_refreshing_its_content_does_not()
    {
        var vm = new AssistantPageViewModel(new PortalUiState());
        var modal = new object();
        vm.Register(modal, () => Task.FromResult("first"), null, kind: "code");
        var version = vm.Version;
        vm.Register(modal, () => Task.FromResult("second"), null, kind: "code");
        Assert.Equal(version, vm.Version);
        vm.Register(modal, () => Task.FromResult("query"), null, kind: "data");
        Assert.True(vm.Version > version);
        Assert.Equal("data", vm.Kind);
    }

    [Fact]
    public async Task Each_question_gets_the_detected_page_agent_and_current_selection()
    {
        var vm = new AssistantPageViewModel(new PortalUiState());
        vm.Navigate(Project + "workflows/22222222-2222-2222-2222-222222222222/canvas");
        vm.Register(new object(), () => Task.FromResult("selected workflow definition"), null);
        using var snapshot = System.Text.Json.JsonDocument.Parse(await vm.ReadAsync());
        Assert.Equal(vm.Url, snapshot.RootElement.GetProperty("url").GetString());
        Assert.Equal("workflow", snapshot.RootElement.GetProperty("agent").GetString());
        Assert.Equal("selected workflow definition", snapshot.RootElement.GetProperty("context").GetString());
        Assert.Contains("workflow", vm.StarterQuestion);
        vm.Navigate(Project + "data");
        Assert.Contains("query", vm.StarterQuestion);
        Assert.DoesNotContain("selected workflow definition", await vm.ReadAsync());
    }

    private const string Project = "project/11111111-1111-1111-1111-111111111111/";
    [Theory]
    [InlineData("", "workspace")]
    [InlineData("settings/models", "settings")]
    [InlineData("/settings/access?tab=roles", "settings")]
    [InlineData("onboarding", "settings")]
    [InlineData("observability?tab=logs", "observability")]
    [InlineData("cluster", "infrastructure")]
    [InlineData("artifacts", "artifacts")]
    [InlineData(Project + "dashboard", "workspace")]
    [InlineData(Project + "dashboard/settings", "agents")]
    [InlineData(Project + "data", "data")]
    [InlineData(Project + "data-search", "data")]
    [InlineData(Project + "workflows", "workflow-manager")]
    [InlineData(Project + "chains", "workflow-manager")]
    [InlineData(Project + "workflows?sort=name", "workflow-manager")]
    [InlineData(Project + "workflows/22222222-2222-2222-2222-222222222222/canvas", "workflow")]
    [InlineData(Project + "workflows/22222222-2222-2222-2222-222222222222/runs", "observability")]
    [InlineData(Project + "agents/markup", "agents")]
    [InlineData(Project + "agents/observability", "observability")]
    [InlineData(Project + "jobs", "infrastructure")]
    [InlineData(Project + "jobs/22222222-2222-2222-2222-222222222222", "code")]
    [InlineData(Project + "tests/22222222-2222-2222-2222-222222222222", "code")]
    public void Selects_the_specialist_from_the_route(string url, string kind) =>
        Assert.Equal(kind, AssistantPageViewModel.AgentForUrl(url));

    [Fact]
    public async Task Navigation_clears_old_editor_actions_and_old_disposal_cannot_remove_the_new_page()
    {
        var vm = new AssistantPageViewModel(new PortalUiState());
        var first = new object();
        vm.Navigate(Project + "data");
        vm.Register(first, () => Task.FromResult("first project schema"), _ => Task.CompletedTask);
        Assert.NotNull(vm.Apply);
        vm.Navigate("settings/models");
        Assert.Null(vm.Apply);
        Assert.DoesNotContain("first project schema", await vm.ReadAsync());
        var second = new object();
        vm.Register(second, () => Task.FromResult("new page"), null);
        var version = vm.Version;
        vm.Unregister(first);
        Assert.Equal(version, vm.Version);
        Assert.Contains("new page", await vm.ReadAsync());
    }

    [Fact]
    public async Task A_late_context_read_is_rejected_after_navigation()
    {
        var vm = new AssistantPageViewModel(new PortalUiState());
        var pending = new TaskCompletionSource<string>();
        vm.Register(new object(), () => pending.Task, null);
        var read = vm.ReadAsync();
        vm.Navigate("observability");
        pending.SetResult("old page data");
        await Assert.ThrowsAsync<InvalidOperationException>(() => read);
    }
}
