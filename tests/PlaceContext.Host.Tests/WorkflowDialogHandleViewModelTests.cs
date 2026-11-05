using Microsoft.JSInterop;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowDialogHandleViewModelTests
{
    [Fact]
    public async Task Dialog_attaches_once_and_removes_listeners_on_close()
    {
        var js = new Js();
        var module = new Module();
        js.Import.SetResult(module);
        var vm = new WorkflowDialogHandleViewModel(js);
        await vm.AfterRenderAsync(true, default);
        await vm.AfterRenderAsync(false, default);
        await vm.DisposeAsync();
        await vm.DisposeAsync();
        Assert.Equal(new[] { "attachDialog", "detachDialog" }, module.Calls);
        Assert.True(module.Disposed);
    }

    [Fact]
    public async Task Closing_before_import_finishes_does_not_attach_to_a_removed_dialog()
    {
        var js = new Js();
        var module = new Module();
        var vm = new WorkflowDialogHandleViewModel(js);
        var opening = vm.AfterRenderAsync(true, default);
        await vm.DisposeAsync();
        js.Import.SetResult(module);
        await opening;
        Assert.Empty(module.Calls);
        Assert.True(module.Disposed);
    }

    private sealed class Js : IJSRuntime
    {
        public TaskCompletionSource<IJSObjectReference> Import { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public async ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args)
        {
            Assert.Equal("import", identifier);
            return (T)(object)await Import.Task;
        }
    }

    private sealed class Module : IJSObjectReference
    {
        public List<string> Calls { get; } = [];
        public bool Disposed { get; private set; }
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args)
        {
            Calls.Add(identifier);
            return ValueTask.FromResult(default(T)!);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
