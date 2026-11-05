using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class WorkflowDialogHandleViewModel(IJSRuntime js) : PageViewModel, IComponentViewModel, IAsyncDisposable
{
    private ElementReference _handle;
    private IJSObjectReference? _module;
    private bool _disposed;

    public async Task AfterRenderAsync(bool firstRender, ElementReference handle)
    {
        if (!firstRender || _disposed) return;
        _handle = handle;
        var module = await js.InvokeAsync<IJSObjectReference>("import", "./Components/Shared/WorkflowCodeWindow.razor.js");
        if (_disposed)
        {
            await module.DisposeAsync();
            return;
        }
        _module = module;
        await module.InvokeVoidAsync("attachDialog", handle);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_module is null) return;
        try
        {
            await _module.InvokeVoidAsync("detachDialog", _handle);
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
    }
}
