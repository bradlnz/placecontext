using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class DatabasesViewModel(IDatabaseConsole databases, PortalUiState ui, IServiceConsole services) : PageViewModel
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private int _version;
    public Guid ProjectId { get; private set; }
    public IReadOnlyList<DatabaseInstance> Databases { get; private set; } = [];
    public IReadOnlyList<ServiceInstance> Apps { get; private set; } = [];
    public string AppId { get; set; } = "";
    public string Search { get; set; } = "";
    public string EngineFilter { get; set; } = "";
    public string Sort { get; set; } = "created";
    private Guid? _selectedId;
    public DatabaseInstance? Selected => VisibleDatabases.FirstOrDefault(item => item.Id == _selectedId);
    public IReadOnlyList<DatabaseInstance> VisibleDatabases => (Sort == "name"
        ? Filtered.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        : Filtered.OrderByDescending(item => item.CreatedAt)).ToList();
    private IEnumerable<DatabaseInstance> Filtered => Databases.Where(item =>
        (EngineFilter.Length == 0 || item.Engine == EngineFilter) &&
        (Search.Trim().Length == 0 || $"{item.Name} {item.Engine} {item.Host} {item.Id}".Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)));
    public void Select(DatabaseInstance item) { if (!Busy) _selectedId = item.Id; }
    public bool Busy { get; private set; }
    public string? Error { get; private set; }
    public bool ShowEditor { get; private set; }
    public string Name { get; set; } = "";
    public string Engine { get; set; } = "postgres";
    public int MemoryMb { get; set; } = 512;
    public decimal Cpus { get; set; } = 1;
    public DatabaseInstance? Inspecting { get; private set; }
    public string Logs { get; private set; } = "";

    public async Task LoadAsync(Guid projectId)
    {
        _version++; ProjectId = projectId; Databases = []; ShowEditor = false; Inspecting = null; Logs = "";
        _selectedId = null; Search = EngineFilter = ""; Sort = "created";
        ui.Set("Databases", "persistent data stores for your services");
        await RefreshAsync();
    }
    public void NewDatabase() { if (Busy) return; Name = ""; AppId = ""; Engine = "postgres"; MemoryMb = 512; Cpus = 1; Error = null; ShowEditor = true; }
    public void CloseEditor() { if (!Busy) ShowEditor = false; }
    public void CloseLogs() { if (!Busy) { Inspecting = null; Logs = ""; } }
    public void DisposePage() { _version++; Logs = ""; Detach(); }
    public Task RefreshAsync() => RunAsync(null);
    public Task CreateAsync()
    {
        if (Busy) return Task.CompletedTask;
        var request = new DatabaseLaunch(Name, Engine, MemoryMb, Cpus, Guid.TryParse(AppId, out var appId) ? appId : null);
        return RunAsync(async (project, version) => { await databases.CreateAsync(project, request); if (_version == version) ShowEditor = false; });
    }
    public Task ControlAsync(DatabaseInstance database, string action) => Busy ? Task.CompletedTask : RunAsync((project, _) => databases.ControlAsync(project, database.Id, action));
    public Task InspectAsync(DatabaseInstance database) => RunAsync(async (project, version) =>
    {
        var logs = await databases.LogsAsync(project, database.Id);
        if (_version == version) { _selectedId = database.Id; Inspecting = database; Logs = logs; }
    });

    private async Task RunAsync(Func<Guid, int, Task>? action)
    {
        var project = ProjectId; var version = _version;
        await _operations.WaitAsync();
        try
        {
            if (version != _version) return;
            Busy = true; Error = null; NotifyStateChanged();
            try { if (action is not null) await action(project, version); }
            catch (Exception ex) { if (version == _version) Error = ex.Message; }
            var list = await databases.ListAsync(project);
            if (version == _version) Databases = list;
            var apps = await services.ListAsync(project);
            if (version == _version) Apps = apps;
        }
        catch (Exception ex) { if (version == _version) Error ??= ex.Message; }
        finally { Busy = false; _operations.Release(); NotifyStateChanged(); }
    }
}
