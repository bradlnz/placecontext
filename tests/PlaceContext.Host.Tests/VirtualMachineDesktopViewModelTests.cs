using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PlaceContext.Application.Workload.VirtualMachines;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class VirtualMachineDesktopViewModelTests
{
    [Fact]
    public async Task Running_desktop_renders_the_locked_preview_without_an_open_button()
    {
        var services = new ServiceCollection().AddLogging().AddSingleton<IVirtualMachineDesktop>(new Desktop())
            .AddSingleton<IJSRuntime>(new Js()).AddTransient<VirtualMachineDesktopViewModel>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<PlaceContext.Host.Components.Shared.VirtualMachineDesktop>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Run"] = new VirtualMachineRunContext(Guid.NewGuid(), Guid.NewGuid(), 0) }));
            return rendered.ToHtmlString();
        });
        Assert.Contains("Live preview", html);
        Assert.Contains("input locked", html);
        Assert.Contains("desktop-screen", html);
        Assert.Contains("Take control", html);
        Assert.DoesNotContain("Open desktop", html);
    }
    private sealed class Desktop : IVirtualMachineDesktop
    {
        public VirtualMachineDesktopStatus State = new("Running");
        public Task<VirtualMachineDesktopStatus?> StatusAsync(VirtualMachineRunContext run, CancellationToken ct = default) => Task.FromResult<VirtualMachineDesktopStatus?>(State);
        public Task TakeControlAsync(VirtualMachineRunContext run, CancellationToken ct = default)
        {
            State = new("Human");
            return Task.CompletedTask;
        }
        public Task ResumeAsync(VirtualMachineRunContext run, CancellationToken ct = default)
        {
            State = new("Running");
            return Task.CompletedTask;
        }
        public Task SaveLoginAsync(VirtualMachineRunContext run, string name, CancellationToken ct = default) => throw new InvalidOperationException("Session could not be saved.");
        public Task<Stream> ConnectAsync(VirtualMachineRunContext run, CancellationToken ct = default, bool viewOnly = false) => throw new NotSupportedException();
    }
    private sealed class Js : IJSRuntime, IJSObjectReference
    {
        public List<string> Calls = [];
        public List<bool> Controls = [];
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args)
        {
            Calls.Add(identifier);
            if (identifier == "connect")
            {
                Controls.Add((bool)args![2]!);
            }
            return ValueTask.FromResult(identifier == "import" ? (T)(object)this : default!);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    [Fact]
    public async Task Live_preview_opens_locked_and_only_unlocks_after_taking_control()
    {
        var desktop = new Desktop();
        var js = new Js();
        await using var vm = new VirtualMachineDesktopViewModel(desktop, js);
        var run = new VirtualMachineRunContext(Guid.NewGuid(), Guid.NewGuid(), 0);
        desktop.State = new("Starting");
        await vm.SetRunAsync(run);
        await vm.AfterRenderAsync();
        Assert.Empty(js.Calls);
        await vm.ResumeAsync();
        await vm.AfterRenderAsync();
        Assert.Equal(new[] { "import", "connect" }, js.Calls);
        Assert.False(js.Controls.Last());
        await vm.TakeControlAsync();
        await vm.AfterRenderAsync();
        Assert.True(js.Controls.Last());
        await vm.SaveLoginAsync();
        Assert.Equal("Session could not be saved.", vm.Error);
        Assert.False(vm.Busy);
        Assert.Equal("Human", vm.Status!.State);
        await vm.ResumeAsync();
        await vm.AfterRenderAsync();
        Assert.False(js.Controls.Last());
        await vm.SetRunAsync(run with
        {
            RunId = Guid.NewGuid()
        });
        Assert.Equal("disconnect", js.Calls.Last());
        Assert.Null(vm.Error);
    }
}
