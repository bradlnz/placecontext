using System.Text.Json;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class AssistantCodeDraftTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Drafts_cannot_overwrite_new_edits_or_a_different_file(bool switchFile)
    {
        var js = new EditorJs();
        var vm = new JobEditorViewModel(new PlaceContextService(new Queries(), null!, null!, null!),
            new PortalUiState(), null!, js, null!);
        await vm.LoadAsync();
        await vm.AfterRenderAsync();
        Assert.Contains("original", await vm.AssistantContextAsync());
        if (switchFile) await vm.SwitchFileAsync(1);
        else js.Value = "new user edits";

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ApplyAssistantCodeAsync("stale replacement"));
        Assert.Equal(0, js.Replacements);

        await vm.AssistantContextAsync();
        await vm.ApplyAssistantCodeAsync("fresh replacement");
        Assert.Equal("fresh replacement", js.Value);
        Assert.Equal("fresh replacement", vm.Files[vm.ActiveIndex].Content);
        Assert.Equal(1, js.Replacements);
        await vm.DisposeAsync();
    }

    private sealed class Queries : IDispatcher
    {
        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default) =>
            Task.FromResult((TResult)(object)JsonSerializer.Deserialize<JobView>("""
                {"Name":"Example","MapSourceKind":"code","MapRuntimeId":"node","MapEntrypoint":"main.js",
                 "MapFiles":[{"Path":"main.js","Content":"original"},{"Path":"other.js","Content":"other file"}]}
                """)!);
        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default) =>
            throw new InvalidOperationException("Applying a draft must not save or run code.");
    }

    private sealed class EditorJs : IJSRuntime
    {
        public string Value = "";
        public int Replacements;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            object? result = default(TValue);
            if (identifier == "pcmonaco.init") { Value = (string)args![1]!; result = true; }
            if (identifier == "pcmonaco.getValue") result = Value;
            if (identifier == "pcmonaco.openFile") Value = (string)args![2]!;
            if (identifier == "pcmonaco.setValue") { Value = (string)args![1]!; Replacements++; }
            return ValueTask.FromResult((TValue)result!);
        }
    }
}
