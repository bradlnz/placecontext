using Microsoft.JSInterop;
using PlaceContext.Application.Operations;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class JobsTemplateRuntimeTests : IDisposable
{
    private readonly EditorJs _js = new();
    private readonly JobsViewModel _vm;

    public JobsTemplateRuntimeTests() =>
        _vm = new JobsViewModel(null!, new OperationCenter(null!, default), _js, null!);

    [Fact]
    public async Task Every_template_switches_to_its_language_specific_source_and_entrypoint()
    {
        foreach (var template in JobTemplateCatalog.All)
        {
            _vm.OpenTemplateModal();
            _vm.SelectTemplate(template);
            _vm.ApplySelectedTemplate();
            Assert.Equal(template.SourcesByRuntime.Keys, _vm.SourceRuntimes);

            foreach (var (runtime, source) in template.SourcesByRuntime)
            {
                await _vm.OnSourceRuntimeChangedAsync(runtime);

                Assert.Equal(runtime, _vm.EdMapRuntimeId);
                Assert.Equal(source, _vm.EdMapSource);
                Assert.Equal(JobsViewModel.DefaultEntrypointFor(runtime), _vm.EdMapEntrypoint);
                Assert.Equal(EditorLanguageCatalog.ForPath(_vm.EdMapEntrypoint), _vm.SourceEditorLanguage);
            }
        }
    }

    [Fact]
    public async Task Switching_back_restores_live_editor_changes_and_custom_filename()
    {
        var template = JobTemplateCatalog.GetById("mysql-query")!;
        _vm.SelectTemplate(template);
        _vm.ApplySelectedTemplate();
        await _vm.AfterRenderAsync();
        _js.Value = "console.log('custom query');";
        _vm.EdMapEntrypoint = "custom.js";

        await _vm.OnSourceRuntimeChangedAsync("python");
        Assert.Equal(template.SourcesByRuntime["python"], _js.Value);
        Assert.Equal("python", _js.Language);
        Assert.Equal("main.py", _vm.EdMapEntrypoint);

        await _vm.OnSourceRuntimeChangedAsync("node");
        Assert.Equal("console.log('custom query');", _js.Value);
        Assert.Equal("javascript", _js.Language);
        Assert.Equal("custom.js", _vm.EdMapEntrypoint);
    }

    [Fact]
    public async Task Unsupported_language_does_not_replace_a_validation_template_with_placeholder_code()
    {
        var template = JobTemplateCatalog.GetById("json-data-quality")!;
        _vm.SelectTemplate(template);
        _vm.ApplySelectedTemplate();

        await _vm.OnSourceRuntimeChangedAsync("node");

        Assert.Equal("python", _vm.EdMapRuntimeId);
        Assert.Equal(template.MapSource, _vm.EdMapSource);
        Assert.Equal("main.py", _vm.EdMapEntrypoint);
        Assert.Equal(new[] { "python" }, _vm.SourceRuntimes);
    }

    [Fact]
    public async Task New_jobs_and_other_templates_do_not_reuse_previous_language_drafts()
    {
        _vm.SelectTemplate(JobTemplateCatalog.GetById("mysql-query")!);
        _vm.ApplySelectedTemplate();
        _vm.EdMapSource = "my custom draft";
        await _vm.OnSourceRuntimeChangedAsync("python");

        _vm.NewJob();
        Assert.Contains("go", _vm.SourceRuntimes);
        Assert.Empty(_vm.EdMapSource);

        var template = JobTemplateCatalog.GetById("webhook-receiver")!;
        _vm.SelectTemplate(template);
        _vm.ApplySelectedTemplate();
        await _vm.OnSourceRuntimeChangedAsync("python");
        await _vm.OnSourceRuntimeChangedAsync("node");
        Assert.Equal(template.SourcesByRuntime["node"], _vm.EdMapSource);
    }

    public void Dispose() => _vm.DetachEvents();

    private sealed class EditorJs : IJSRuntime
    {
        public string Value = "";
        public string Language = "";

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "pcmonaco.getValue")
            {
                return ValueTask.FromResult((TValue)(object)Value);
            }
            if (identifier is "pcmonaco.init" or "pcmonaco.setValue")
            {
                Value = (string)args![1]!;
                Language = (string)args[2]!;
                return ValueTask.FromResult(identifier == "pcmonaco.init" ? (TValue)(object)true : default!);
            }
            throw new NotSupportedException(identifier);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
