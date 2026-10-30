using Microsoft.JSInterop;

namespace PlaceContext.Host.Tests;

internal sealed class DashboardTestJs : IJSRuntime
{
    public int Renders { get; private set; }
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    { if (identifier == "pcchart.renderAll") Renders++; return ValueTask.FromResult(default(TValue)!); }
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
}
