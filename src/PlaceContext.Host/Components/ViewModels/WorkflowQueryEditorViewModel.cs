using Microsoft.JSInterop;
using PlaceContext.Application.ProjectData;

namespace PlaceContext.Host.Components.ViewModels;

/// <summary>
/// Hosts the Monaco/plain-text SQL editor used by the workflow Query data studio. The component
/// stays presentational; this ViewModel owns the JS interop and tolerates Monaco still loading
/// (or never loading when the CDN is unavailable).
/// </summary>
public sealed class WorkflowQueryEditorViewModel(IJSRuntime js) : PageViewModel, IComponentViewModel
{
    private bool _initialized;
    private bool _monaco = true;
    private string? _schemaSignature;

    public string EditorId { get; } = "workflow-query-" + Guid.NewGuid().ToString("N");

    public async Task InitAsync(string value, string label)
    {
        if (_initialized)
        {
            return;
        }
        try
        {
            _monaco = await js.InvokeAsync<bool>(
                "pcmonaco.init", EditorId, value, "sql", null, "query.sql", false, label);
        }
        catch
        {
            _monaco = false;
        }
        _initialized = true;
    }

    /// <summary>
    /// Monaco's SQL provider is global, so the schema is pushed for every SQL editor in the app.
    /// Only push when the project's table set actually changed to keep renders cheap.
    /// </summary>
    public async Task PushSchemaAsync(IReadOnlyList<ProjectTableSchema> tables)
    {
        if (!_monaco || tables.Count == 0)
        {
            return;
        }
        var signature = string.Join(
            '\u001f',
            tables.Select(table =>
                table.Name + ':' + string.Join(',', table.Columns.Select(column => column.Name))));
        if (signature == _schemaSignature)
        {
            return;
        }
        try
        {
            await js.InvokeVoidAsync("pcmonaco.setSqlSchema", new
            {
                tables = tables.Select(table => new
                {
                    name = table.Name,
                    columns = table.Columns.Select(column => new
                    {
                        name = column.Name,
                        type = column.Type,
                    }).ToList(),
                }).ToList(),
            });
            _schemaSignature = signature;
        }
        catch
        {
            _monaco = false;
        }
    }

    public async Task<string> ReadAsync(string fallback)
    {
        if (!_initialized)
        {
            return fallback;
        }
        try
        {
            return await js.InvokeAsync<string?>("pcmonaco.getValue", EditorId) ?? fallback;
        }
        catch
        {
            _monaco = false;
            return fallback;
        }
    }

    public async Task SetValueAsync(string value)
    {
        if (!_initialized)
        {
            return;
        }
        try
        {
            await js.InvokeVoidAsync("pcmonaco.setValue", EditorId, value, "sql");
        }
        catch
        {
            _monaco = false;
        }
    }

    /// <summary>Inserts at the caret and returns the editor's new text (or the fallback text while
    /// Monaco is still upgrading from the plain-text editor).</summary>
    public async Task<string?> InsertAsync(string text, string fallback)
    {
        if (!_initialized)
        {
            return fallback + text;
        }
        try
        {
            var value = await js.InvokeAsync<string?>("pcmonaco.insertText", EditorId, text);
            return value ?? fallback + text;
        }
        catch
        {
            _monaco = false;
            return fallback + text;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await js.InvokeVoidAsync("pcmonaco.destroy", EditorId);
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }
}
