using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Operations;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class ObservabilityTabTests
{
    [Fact]
    public async Task Switching_tabs_clears_details_and_deep_links_but_preserves_other_query_parameters()
    {
        var navigation = new Navigation();
        var vm = new ObservabilityViewModel(null!, null!, navigation, null!, null!, null!, null!);
        await vm.OpenChainRun(new(Guid.NewGuid(), "Demo", new(Guid.NewGuid(), Guid.NewGuid(), "chain", "Succeeded", [], null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));
        vm.RunId = Guid.NewGuid().ToString();
        vm.ChainRunId = Guid.NewGuid().ToString();
        vm.SelectTab(ObservabilityViewModel.LogsTab);
        Assert.Null(vm.OpenChain);
        Assert.Null(vm.Open);
        Assert.Null(vm.OpenTelemetry);
        Assert.Null(vm.RunId);
        Assert.Null(vm.ChainRunId);
        Assert.Contains("tab=logs", navigation.Uri);
        Assert.Contains("keep=yes", navigation.Uri);
        Assert.DoesNotContain("chainRun=", navigation.Uri);
        Assert.DoesNotContain("workflowRun=", navigation.Uri);
        Assert.DoesNotContain("run=", navigation.Uri);
    }

    [Fact]
    public void Closing_a_deep_link_replaces_its_url_so_it_cannot_reopen_on_refresh()
    {
        var navigation = new Navigation();
        var vm = new ObservabilityViewModel(null!, null!, navigation, null!, null!, null!, null!);
        vm.CloseRun();
        Assert.True(navigation.Replaced);
        Assert.DoesNotContain("run=", navigation.Uri);
        Assert.DoesNotContain("chainRun=", navigation.Uri);
        Assert.DoesNotContain("workflowRun=", navigation.Uri);
    }

    [Fact]
    public async Task Reentering_observability_remounts_the_editor_in_the_same_circuit()
    {
        var js = new EditorJs();
        var vm = new ObservabilityViewModel(null!, new PortalUiState(), new Navigation(),
            new OperationCenter(null!, default), null!, null!, js);
        await vm.InitializeAsync();
        vm.SelectTab(ObservabilityViewModel.LogsTab);
        await vm.AfterRenderAsync();
        Assert.Equal(1, js.Initializations);
        vm.Dispose();
        await vm.InitializeAsync();
        vm.SelectTab(ObservabilityViewModel.LogsTab);
        await vm.AfterRenderAsync();
        Assert.Equal(2, js.Initializations);
        vm.Dispose();
    }

    [Theory]
    [InlineData("workflows")]
    [InlineData("chains")]
    public async Task Workflow_tabs_restore_new_and_legacy_urls_but_emit_canonical_urls(string tab)
    {
        var navigation = new Navigation();
        var vm = new ObservabilityViewModel(null!, null!, navigation, null!, null!, null!, null!);
        await vm.RestoreTabAsync(tab);
        Assert.True(vm.IsChainsTab);
        Assert.Equal("workflows", vm.Tab);
        vm.SelectChainsTab();
        Assert.Contains("tab=workflows", navigation.Uri);
        Assert.DoesNotContain("workflowRun=", navigation.Uri);
        Assert.DoesNotContain("chainRun=", navigation.Uri);
    }

    private sealed class EditorJs : IJSRuntime
    {
        public int Initializations;
        private bool _mounted;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args)
        {
            if (identifier == "pcmonaco.init")
            {
                Initializations++;
                _mounted = true;
            }
            if (identifier == "pcmonaco.destroy")
            {
                _mounted = false;
            }
            object? value = typeof(TValue) == typeof(bool) ? _mounted : typeof(TValue) == typeof(string) ? "logs" : default(TValue);
            return ValueTask.FromResult((TValue)value!);
        }
    }

    private sealed class Navigation : NavigationManager
    {
        public bool Replaced;
        public Navigation() => Initialize("http://localhost/", "http://localhost/observability?run=123&chainRun=456&workflowRun=789&keep=yes");
        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Replaced = options.ReplaceHistoryEntry;
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }
}
