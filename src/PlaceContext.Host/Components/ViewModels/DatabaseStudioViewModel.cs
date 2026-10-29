using Microsoft.JSInterop;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class DatabaseStudioViewModel(IDatabaseConsole databases, IJSRuntime js) : PageViewModel, IComponentViewModel, IAsyncDisposable
{
    public Guid ProjectId { get; private set; }
    public Guid DatabaseId { get; private set; }
    public DatabaseInstance? Database { get; private set; }
    public string EditorId { get; } = $"database-query-{Guid.NewGuid():N}";
    public string Query { get; set; } = "";
    public string Search { get; set; } = "";
    public bool Busy { get; private set; }
    public string? Error { get; private set; }
    public DatabaseQueryResult? Result { get; private set; }
    public IReadOnlyList<(string Name, string Query)> Resources { get; private set; } = [];
    public IEnumerable<(string Name, string Query)> VisibleResources => Resources.Where(item => item.Name.Contains(Search, StringComparison.OrdinalIgnoreCase));
    private bool _mounted;
    private bool _mounting;
    private bool _disposed;
    private int _version;
    private const string TablesQuery = "SELECT table_schema, table_name FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY table_schema, table_name LIMIT 500";

    public async Task LoadAsync(Guid project, Guid database)
    {
        var version = ++_version; ProjectId = project; DatabaseId = database; Database = null; Result = null; Resources = []; Error = null; Busy = true;
        try
        {
            var selected = (await databases.ListAsync(project)).FirstOrDefault(item => item.Id == database) ?? throw new KeyNotFoundException("Database not found.");
            if (version != _version) return;
            Database = selected;
            Query = selected.Engine == "postgres" ? TablesQuery : "SCAN *";
            var resources = await databases.QueryAsync(project, database, Query);
            if (version != _version) return;
            Resources = selected.Engine == "postgres"
                ? resources.Rows.Select(row => ($"{row[0]}.{row[1]}", $"SELECT * FROM {Quote(row[0]!)}.{Quote(row[1]!)} LIMIT 100")).ToList()
                : resources.Rows.Select(row => (row[0]!, "GET " + row[0])).ToList();
            Result = resources;
            if (_mounted) await js.InvokeVoidAsync("pcmonaco.setValue", EditorId, Query, selected.Engine == "postgres" ? "sql" : "plaintext");
        }
        catch (Exception ex) { if (version == _version) Error = ex.Message; }
        finally { if (version == _version) { Busy = false; NotifyStateChanged(); } }
    }
    internal static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    public Task SelectAsync(string query) => ExecuteAsync(query, false);
    public Task RunAsync(bool next = false) => ExecuteAsync(null, next);
    private async Task ExecuteAsync(string? selectedQuery, bool next)
    {
        if (Busy || Database is null) return;
        var version = _version; var project = ProjectId; var database = DatabaseId;
        Busy = true; Error = null; NotifyStateChanged();
        try
        {
            if (selectedQuery is not null)
            {
                Query = selectedQuery;
                await js.InvokeVoidAsync("pcmonaco.setValue", EditorId, Query, Database.Engine == "postgres" ? "sql" : "plaintext");
            }
            else if (_mounted) Query = await js.InvokeAsync<string?>("pcmonaco.getValue", EditorId) ?? Query;
            var result = await databases.QueryAsync(project, database, Query, next ? Result?.Cursor : null);
            if (version == _version) Result = result;
        }
        catch (Exception ex) { if (version == _version) { Error = ex.Message; Result = null; } }
        finally { if (version == _version) { Busy = false; NotifyStateChanged(); } }
    }
    public async Task AfterRenderAsync()
    {
        if (_mounting || _disposed || Database is null) return;
        _mounting = true;
        try
        {
            _mounted = await js.InvokeAsync<bool>("pcmonaco.isMounted", EditorId);
            if (_mounted) return;
            await js.InvokeAsync<bool>("pcmonaco.init", EditorId, Query, Database.Engine == "postgres" ? "sql" : "plaintext");
            // init returns false for a working fallback textarea, so check the actual element.
            _mounted = await js.InvokeAsync<bool>("pcmonaco.isMounted", EditorId);
        }
        catch (JSException) { _mounted = false; }
        finally { _mounting = false; }
    }
    public async ValueTask DisposeAsync()
    {
        _disposed = true; _version++; Detach();
        try { if (_mounted) await js.InvokeVoidAsync("pcmonaco.destroy", EditorId); }
        catch (JSDisconnectedException) { }
    }
}
