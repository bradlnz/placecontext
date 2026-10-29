using Microsoft.JSInterop;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class RunOutputViewModelTests
{
    [Fact]
    public async Task Large_output_is_bounded_before_js_interop_and_changes_outside_the_preview_do_not_reload_it()
    {
        var js = new Js();
        var value = new string('x', 1_000_000);
        var vm = new RunOutputViewModel(js) { Value = value };
        await vm.AfterRenderAsync();
        Assert.True(vm.Truncated);
        Assert.Equal(value[..RunOutputViewModel.MaxPreviewChars], js.Calls[0].Args[1]);
        Assert.Equal("plaintext", js.Calls[0].Args[2]);
        Assert.Equal(value, vm.Value);
        vm.Value += "tail update";
        await vm.AfterRenderAsync();
        Assert.Single(js.Calls);
        vm.Value = "{\"short\":true}";
        await vm.AfterRenderAsync();
        Assert.False(vm.Truncated);
        Assert.Equal(vm.Value, js.Calls[1].Args[1]);
        Assert.Equal("json", js.Calls[1].Args[2]);
    }

    [Fact]
    public async Task Preview_does_not_split_a_surrogate_pair()
    {
        var js = new Js();
        var vm = new RunOutputViewModel(js) { Value = new string('x', RunOutputViewModel.MaxPreviewChars - 1) + "😀tail" };
        await vm.AfterRenderAsync();
        Assert.Equal(new string('x', RunOutputViewModel.MaxPreviewChars - 1), js.Calls[0].Args[1]);
    }

    [Theory]
    [InlineData("json", "Run output")]
    [InlineData("shell", "Queue integration request")]
    public async Task Output_is_read_only_updates_only_on_change_and_disposes_its_editor(string language, string label)
    {
        var js = new Js();
        var vm = new RunOutputViewModel(js) { Value = "{\"result\":1}", Language = language, Label = label };
        await vm.AfterRenderAsync();
        var init = Assert.Single(js.Calls);
        Assert.Equal("pcmonaco.init", init.Name);
        Assert.Equal(vm.Value, init.Args[1]);
        Assert.Equal(language, init.Args[2]);
        Assert.Equal(true, init.Args[5]);
        Assert.Equal(label, init.Args[6]);
        await vm.AfterRenderAsync();
        Assert.Single(js.Calls);
        vm.Value = "{\"result\":2}";
        await vm.AfterRenderAsync();
        Assert.Equal("pcmonaco.setValue", js.Calls[1].Name);
        Assert.Equal(vm.Value, js.Calls[1].Args[1]);
        Assert.Equal(language, js.Calls[1].Args[2]);
        await vm.DisposeAsync();
        Assert.Equal("pcmonaco.destroy", js.Calls[2].Name);
        Assert.Equal(vm.EditorId, js.Calls[2].Args[0]);
        Assert.NotEqual(vm.EditorId, new RunOutputViewModel(js).EditorId);
    }

    [Fact]
    public void Navigation_opens_one_group_at_a_time_and_allows_collapsing_it()
    {
        var vm = new MainLayoutViewModel(null!, null!, null!, null!, null!, null!, null!);
        vm.ToggleGroup("Automation");
        Assert.True(vm.IsExpanded("Automation"));
        vm.ToggleGroup("Infrastructure");
        Assert.False(vm.IsExpanded("Automation"));
        Assert.True(vm.IsExpanded("Infrastructure"));
        vm.ToggleGroup("Infrastructure");
        Assert.False(vm.IsExpanded("Infrastructure"));
    }

    private sealed class Js : IJSRuntime
    {
        public List<(string Name, object?[] Args)> Calls { get; } = [];
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => InvokeAsync<T>(name, default, args);
        public ValueTask<T> InvokeAsync<T>(string name, CancellationToken ct, object?[]? args)
        {
            Calls.Add((name, args ?? []));
            return ValueTask.FromResult(default(T)!);
        }
    }
}
