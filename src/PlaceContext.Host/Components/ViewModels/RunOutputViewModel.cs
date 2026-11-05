using Microsoft.JSInterop;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class RunOutputViewModel(IJSRuntime js) : PageViewModel, IComponentViewModel, IAsyncDisposable
{
    public string EditorId { get; } = $"run-output-{Guid.NewGuid():N}";
    public string Value { get; set; } = "";
    public string Language { get; set; } = "json";
    public string Label { get; set; } = "Run output";
    private (string Value, string Language, string Label)? _rendered;

    public async Task AfterRenderAsync()
    {
        var current = (Value, Language, Label);
        if (_rendered == current)
        {
            return;
        }
        async Task ApplyOutputAsync()
        {
            if (_rendered is null || _rendered.Value.Label != Label)
            {
                await js.InvokeAsync<bool>("pcmonaco.init", EditorId, current.Value, current.Language, null, null, true, current.Label);
                return;
            }
            await js.InvokeVoidAsync("pcmonaco.setValue", EditorId, current.Value, current.Language);
        }
        await ApplyOutputAsync();
        _rendered = current;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await js.InvokeVoidAsync("pcmonaco.destroy", EditorId);
        }
        catch (JSDisconnectedException) { }
    }
}
