using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ServicesViewModel(IServiceConsole services, PortalUiState ui, IDatabaseConsole? databases = null, IChainQueueService? queues = null) : PageViewModel
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private int _version;
    public Guid ProjectId { get; private set; }
    public Guid? DetailServiceId { get; private set; }
    public string ListUrl => $"/project/{ProjectId}/services";
    public string ServiceUrl(Guid id) => $"{ListUrl}/{id}";
    public IReadOnlyList<ServiceInstance> Services { get; private set; } = [];
    public string Search { get; set; } = "";
    public string StatusFilter { get; set; } = "";
    public string Sort { get; set; } = "created";
    private Guid? _selectedId;
    public ServiceInstance? Selected => (DetailServiceId.HasValue ? Services : VisibleServices).FirstOrDefault(item => item.Id == _selectedId);
    public IEnumerable<DatabaseInstance> AppDatabases => Databases.Where(database => database.ServiceId == DetailServiceId);
    public IReadOnlyList<ServiceInstance> VisibleServices => (Sort == "name"
        ? Filtered.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        : Filtered.OrderByDescending(item => item.CreatedAt)).ToList();
    private IEnumerable<ServiceInstance> Filtered => Services.Where(item =>
        (StatusFilter.Length == 0 || item.Status == StatusFilter) &&
        (Search.Trim().Length == 0 || $"{item.Name} {item.Image} {item.Id}".Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)));
    public void Select(ServiceInstance item) { if (!Busy) _selectedId = item.Id; }
    public bool Busy { get; private set; }
    public string? Error { get; private set; }
    public bool ShowEditor { get; private set; }
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public string Kind { get; set; } = "web";
    public bool RequireSso { get; set; } = true;
    public int Port { get; set; } = 8080;
    public int MemoryMb { get; set; } = 256;
    public decimal Cpus { get; set; } = 1;
    public string Environment { get; set; } = "";
    public IReadOnlyList<DatabaseInstance> Databases { get; private set; } = [];
    public string PostgreSqlId { get; set; } = "";
    public string RedisId { get; set; } = "";
    public string QueueId { get; set; } = "";
    public IReadOnlyList<ChainQueueDefinition> Queues { get; private set; } = [];
    public ServiceInstance? Removing { get; private set; }
    public ServiceInstance? Inspecting { get; private set; }
    public string Logs { get; private set; } = "";

    public async Task LoadAsync(Guid projectId, Guid? serviceId = null, string? tab = null)
    {
        var version = ++_version;
        ProjectId = projectId;
        DetailServiceId = serviceId;
        Services = [];
        _selectedId = serviceId; Search = StatusFilter = ""; Sort = "created";
        Databases = [];
        PostgreSqlId = RedisId = QueueId = "";
        ShowEditor = false;
        Environment = "";
        Removing = Inspecting = null;
        Logs = "";
        ui.Set("Services", "long-running web apps and workers");
        await RefreshAsync();
        if (version == _version && tab != "databases" && Selected is { } selected)
            await InspectAsync(selected);
    }

    public async Task NewService()
    {
        if (Busy) return;
        Name = Image = Environment = "";
        PostgreSqlId = RedisId = QueueId = "";
        Kind = "web"; RequireSso = true; Port = 8080; MemoryMb = 256; Cpus = 1;
        ShowEditor = true;
        Error = null;
        await RefreshAsync();
    }
    public void CloseEditor() { if (!Busy) { ShowEditor = false; Environment = ""; } }
    public void ConfirmRemove(ServiceInstance service) { if (!Busy) Removing = service; }
    public void CancelRemove() { if (!Busy) Removing = null; }
    public void CloseLogs() { if (!Busy) { Inspecting = null; Logs = ""; } }
    public void DisposePage() { _version++; Environment = Logs = ""; Inspecting = Removing = null; Detach(); }

    public Task RefreshAsync() => RunAsync(null);
    public Task LaunchAsync()
    {
        if (Busy) return Task.CompletedTask;
        var ids = new[] { PostgreSqlId, RedisId }.Where(value => Guid.TryParse(value, out _)).Select(Guid.Parse).ToArray();
        var request = new ServiceLaunch(Name, Image, Kind == "worker" ? null : Port, MemoryMb, Cpus, Environment, ids, Kind == "web" && RequireSso, Guid.TryParse(QueueId, out var queueId) ? [queueId] : []);
        return RunAsync(async (project, version) =>
        {
            await services.LaunchAsync(project, request);
            if (version == _version) { ShowEditor = false; Environment = ""; }
        });
    }
    public Task ControlAsync(ServiceInstance service, string action) => Busy ? Task.CompletedTask : RunAsync(async (project, version) =>
    {
        await services.ControlAsync(project, service.Id, action);
        if (version == _version && action == "remove")
        {
            Removing = null;
            if (Inspecting?.Id == service.Id) { Inspecting = null; Logs = ""; }
        }
    });
    public Task RemoveAsync() => Removing is { } service ? ControlAsync(service, "remove") : Task.CompletedTask;
    public Task InspectAsync(ServiceInstance service) => RunAsync(async (project, version) =>
    {
        var logs = await services.LogsAsync(project, service.Id);
        if (version == _version) { _selectedId = service.Id; Inspecting = service; Logs = logs; }
    });

    private async Task RunAsync(Func<Guid, int, Task>? action)
    {
        var project = ProjectId;
        var version = _version;
        await _operations.WaitAsync();
        try
        {
            if (version != _version) return;
            Busy = true;
            Error = null;
            NotifyStateChanged();
            try { if (action is not null) await action(project, version); }
            catch (Exception ex) { if (version == _version) Error = ex.Message; }
            var list = await services.ListAsync(project);
            if (version == _version) Services = list;
            if (queues is not null && ShowEditor)
            {
                var availableQueues = await queues.ListAsync(project);
                if (version == _version) Queues = availableQueues;
            }
            if (databases is not null && (ShowEditor || DetailServiceId.HasValue))
            {
                var stores = await databases.ListAsync(project);
                if (version == _version) Databases = stores;
            }
        }
        catch (Exception ex) { if (version == _version) Error ??= ex.Message; }
        finally { Busy = false; _operations.Release(); NotifyStateChanged(); }
    }
}
