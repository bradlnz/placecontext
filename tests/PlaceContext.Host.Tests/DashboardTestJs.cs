using Microsoft.JSInterop;
using System.Text.Json;

namespace PlaceContext.Host.Tests;

internal sealed class DashboardTestJs : IJSRuntime
{
    public int Renders { get; private set; }
    public IReadOnlyList<string> LastRenderedIds { get; private set; } = [];
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        if (identifier == "pcchart.renderAll")
        {
            Renders++;
            var entries = JsonSerializer.SerializeToElement(Assert.Single(args!));
            Assert.Equal(JsonValueKind.Array, entries.ValueKind);
            LastRenderedIds = entries.EnumerateArray().Select(entry => entry.GetProperty("id").GetString()!).ToArray();
        }
        return ValueTask.FromResult(default(TValue)!);
    }
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
}
