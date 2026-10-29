using Microsoft.JSInterop;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class RunOutputViewModel(IJSRuntime js) : PageViewModel, IComponentViewModel, IAsyncDisposable
{
    public const int MaxPreviewChars = 32_000;
    public string EditorId { get; } = $"run-output-{Guid.NewGuid():N}";
    public string Value { get; set; } = "";
    public string Language { get; set; } = "json";
    public string Label { get; set; } = "Run output";
    public bool Truncated => Value.Length > MaxPreviewChars;
    private (string Value, string Language, string Label)? _rendered;

    public async Task AfterRenderAsync()
    {
        var length = Truncated && char.IsHighSurrogate(Value[MaxPreviewChars - 1]) ? MaxPreviewChars - 1 : MaxPreviewChars;
        var current = (Value: Truncated ? Value[..length] : Value, Language: Truncated ? "plaintext" : Language, Label);
        if (_rendered == current) return;
        if (_rendered is null || _rendered.Value.Label != Label)
        {
            await js.InvokeAsync<bool>("pcmonaco.init", EditorId, current.Value, current.Language, null, null, true, current.Label);
        }
        else
            await js.InvokeVoidAsync("pcmonaco.setValue", EditorId, current.Value, current.Language);
        _rendered = current;
    }

    public async ValueTask DisposeAsync()
    {
        try { await js.InvokeVoidAsync("pcmonaco.destroy", EditorId); }
        catch (JSDisconnectedException) { }
    }
}
