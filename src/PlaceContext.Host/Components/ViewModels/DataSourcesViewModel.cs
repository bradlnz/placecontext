using System.Text.Json;
using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Features;
using PlaceContext.Application.Jobs;
using PlaceContext.Host.Components.Models;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class DataSourcesViewModel(IDispatcher dispatcher, PortalUiState ui,
    IConfiguration configuration, NavigationManager navigation) : PageViewModel
{
    public IReadOnlyList<SourceProvider> ConnectableProviders { get; } = SourceProviders.All
        .Where(provider => provider.Id != "email")
        .ToArray();
    private int _generation;
    private CancellationTokenSource? _statusPolling;
    public int SetupStep { get; private set; } = 1;
    public string? StatusError { get; private set; }
    public bool CanConnectNext => Datasets.Count > 0 && !string.IsNullOrWhiteSpace(Name);
    public bool CanSave => ShowConnect && SetupStep == 3 && FieldsApproved && CanConnectNext && QueueId.HasValue;
    public string ExecutionStatus => SelectedSource?.Execution is { Status: "running" } run && run.StartedAt < DateTimeOffset.UtcNow.AddMinutes(-10)
        ? "interrupted" : SelectedSource?.Execution?.Status ?? "waiting";
    public IReadOnlyList<SourceSetupTile> SetupTiles =>
    [
        new(1, "Connection", "Choose and connect a source", SetupStep == 1 ? "active" : "complete"),
        new(2, "Fields", "Select the data to import", SetupStep == 2 ? "active" : SetupStep > 2 ? "complete" : "pending"),
        new(3, "Prepare & queue", "Sanitise and route data", SetupStep == 3 ? "active" : "pending")
    ];

    public bool CanOpenStep(int step) => !Busy && ShowConnect && step is >= 1 and <= 3
        && (step == 1 || CanConnectNext && (step == 2 || FieldsApproved));

    public void OpenStep(int step)
    {
        if (CanOpenStep(step))
        {
            SetupStep = step;
        }
    }

    public void ContinueSetup() => OpenStep(SetupStep + 1);

    public void StartStatusPolling()
    {
        if (_statusPolling is not null)
        {
            return;
        }
        _statusPolling = new CancellationTokenSource();
        _ = PollStatusAsync(_statusPolling.Token);
    }

    public void StopStatusPolling()
    {
        _statusPolling?.Cancel();
        _statusPolling?.Dispose();
        _statusPolling = null;
    }

    private async Task PollStatusAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (ShowConnect)
                {
                    continue;
                }
                try
                {
                    var changed = await LoadAsync(ct);
                    if (StatusError is not null)
                    {
                        StatusError = null;
                        changed = true;
                    }
                    if (changed)
                    {
                        NotifyStateChanged();
                    }
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    if (StatusError is null)
                    {
                        StatusError = "Live status is unavailable. Showing the last known progress.";
                        NotifyStateChanged();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }
    public Guid ProjectId { get; private set; }
    public IReadOnlyList<DataSourceDefinition> Sources { get; private set; } = [];
    public string Provider { get; private set; } = "postgres";
    public SourceProvider ProviderInfo => SourceProviders.Get(Provider);
    public Dictionary<string, string> ConnectionFields { get; private set; } = [];
    public IReadOnlyList<SourceDataset> Datasets { get; private set; } = [];
    public int DatasetIndex { get; set; }
    public string? FeedToken { get; private set; }
    public Guid? FeedSourceId { get; private set; }
    public string Name { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string Schema { get; set; } = "public";
    public string Table { get; set; } = "";
    public string KeyColumn { get; set; } = "id";
    public string Columns { get; set; } = "id,title,content";
    public string Tags { get; set; } = "";
    public bool EnableEmbeddings { get; set; }
    public bool FieldsApproved { get; set; }
    public Guid? QueueId { get; set; }
    public IReadOnlyList<ChainQueueDefinition> Queues { get; private set; } = [];
    public string? QueueError { get; private set; }
    public string RequiredFields { get; set; } = "";
    public string RedactedFields { get; set; } = "";
    public bool TrimText { get; set; } = true;
    public int BatchSize { get; set; } = 10;
    public bool EditingQueue { get; private set; }
    public string Query { get; set; } = "";
    public Guid SelectedSourceId { get; set; }
    public bool ShowConnect { get; private set; }
    public DataSourceDefinition? SelectedSource => Sources.FirstOrDefault(source => source.Id == SelectedSourceId);
    public bool Busy { get; private set; }
    public string? Error { get; private set; }
    public string? Notice { get; private set; }
    public Guid? PendingDelete { get; set; }
    public string? WorkflowUrl { get; private set; }
    public DataSourceSearchResult? SearchResult { get; private set; }
    public IReadOnlyList<SourceRecord>? Preview { get; private set; }

    public void OpenConnect() => OpenConnect("postgres");

    public Task OpenConnectAsync() => OpenConnectAsync("postgres");

    public Task OpenConnectAsync(string provider)
    {
        OpenConnect(provider);
        return LoadQueuesAsync();
    }

    public void OpenConnect(string provider)
    {
        ShowConnect = true;
        SetupStep = 1;

        Name = ConnectionString = Table = Tags = "";
        Schema = "public";
        KeyColumn = "id";
        Columns = "id,title,content";
        FieldsApproved = EnableEmbeddings = false;
        QueueId = null;
        RequiredFields = RedactedFields = "";
        TrimText = true;
        BatchSize = 10;
        EditingQueue = false;
        SetProvider(provider);
        Error = Notice = null;
    }

    public void CloseConnect()
    {
        ShowConnect = false;
        ConnectionString = "";
        ConnectionFields.Clear();
        Datasets = [];
        FieldsApproved = EnableEmbeddings = false;
    }

    public void SetProvider(string provider)
    {
        SetupStep = 1;
        Error = Notice = null;

        Provider = SourceProviders.Get(provider).Id;
        ConnectionFields = ProviderInfo.Fields.ToDictionary(field => field.Key, field => field.Default);
        ConnectionString = "";
        Datasets = [];
        FieldsApproved = false;
        if (ProviderInfo.Kind == "feed")
        {
            Datasets = [Provider == "email" ? new("feed", "email", "id", ["id", "from", "to", "subject", "text", "receivedAt"])
                : new("feed", "events", "id", ["id", "content"])];
            SelectDataset(0);
        }
    }

    public void SelectDataset(int index)
    {
        if (index < 0 || index >= Datasets.Count)
        {
            return;
        }
        DatasetIndex = index;
        var selected = Datasets[index];
        Schema = selected.Schema;
        Table = selected.Table;
        KeyColumn = selected.KeyColumn;
        Columns = string.Join(", ", selected.Columns);
        InvalidateFieldApprovals();
        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = ProviderInfo.Name + " · " + selected.Table;
        }
    }

    public void InvalidateFieldApprovals() => FieldsApproved = false;

    public void SetConnectionField(string key, string? value)
    {
        SetupStep = 1;

        ConnectionFields[key] = value ?? "";
        Datasets = [];
        FieldsApproved = false;
    }

    public Task DiscoverAsync() => PerformAsync(async () =>
    {
        var project = ProjectId;
        Datasets = [];
        FieldsApproved = false;
        var datasets = await dispatcher.Query(new DiscoverDataSourceQuery(project, Provider, JsonSerializer.Serialize(ConnectionFields)));
        if (project != ProjectId)
        {
            return;
        }
        Datasets = datasets;
        SelectDataset(0);
        SetupStep = datasets.Count > 0 ? 2 : 1;
        Notice = datasets.Count == 0 ? "No datasets found with this account." : "Connection verified. Choose a dataset and approve its fields.";
    });

    public void SelectSource(Guid id)
    {
        if (Busy || SelectedSourceId == id || !Sources.Any(source => source.Id == id))
        {
            return;
        }
        SelectedSourceId = id;

        Preview = null;
        SearchResult = null;
        PendingDelete = null;
        WorkflowUrl = null;
        Query = "";
        EditingQueue = false;
    }

    public async Task InitializeAsync(Guid projectId)
    {
        if (projectId != ProjectId)
        {
            StopStatusPolling();
            _generation++;

            StatusError = null;
            SetupStep = 1;
            Busy = false;
            ShowConnect = false;
            FieldsApproved = false;
            EnableEmbeddings = false;
            Sources = [];
            SearchResult = null;
            Preview = null;
            SelectedSourceId = Guid.Empty;
            ConnectionString = "";
            ConnectionFields.Clear();
            Datasets = [];
            FeedToken = null;
            FeedSourceId = null;
            WorkflowUrl = null;
            PendingDelete = null;
            EditingQueue = false;
            Queues = [];
            QueueId = null;
        }
        ProjectId = projectId;
        ui.Set("Sources", "Connected datasets and retrieval");
        await PerformAsync(() => LoadAsync());
    }

    public async Task LoadQueuesAsync()
    {
        var project = ProjectId;
        QueueError = null;
        try
        {
            var queues = await dispatcher.Query(new ListSourceQueuesQuery(project));
            if (project == ProjectId)
            {
                Queues = queues;
            }
        }
        catch
        {
            if (project == ProjectId)
            {
                Queues = [];
                QueueError = "Queues could not be loaded. Workflow management permission is required.";
            }
        }
        NotifyStateChanged();
    }

    public async Task EditQueueAsync()
    {
        if (SelectedSource is not { } source || Busy)
        {
            return;
        }
        QueueId = source.Queue?.QueueId;
        RequiredFields = string.Join(", ", source.Queue?.RequiredFields ?? []);
        RedactedFields = string.Join(", ", source.Queue?.RedactedFields ?? []);
        TrimText = source.Queue?.TrimText ?? true;
        BatchSize = source.Queue?.BatchSize ?? 10;
        EditingQueue = true;
        await LoadQueuesAsync();
    }

    public void CloseQueueEditor() => EditingQueue = false;

    private SourceQueueConfiguration QueueConfiguration() => new(QueueId ?? throw new ArgumentException("Choose a destination queue."),
        Split(RequiredFields), Split(RedactedFields), TrimText, BatchSize: BatchSize);

    public Task SaveQueueAsync() => PerformAsync(async () =>
    {
        var project = ProjectId;
        await dispatcher.Send(new ConfigureSourceQueueCommand(project, SelectedSourceId, QueueConfiguration()));
        if (project != ProjectId)
        {
            return;
        }
        EditingQueue = false;
        Notice = "Preparation and queue saved. Sync the source to deliver sanitised records.";
        await LoadAsync();
    });

    private async Task<bool> LoadAsync(CancellationToken ct = default)
    {
        var projectId = ProjectId;
        var sources = await dispatcher.Query(new ListDataSourcesQuery(projectId), ct);
        if (projectId != ProjectId)
        {
            return false;
        }
        var selectedId = sources.Any(source => source.Id == SelectedSourceId)
            ? SelectedSourceId : sources.FirstOrDefault()?.Id ?? Guid.Empty;
        if (selectedId == SelectedSourceId && Sources.Select(source => (source.Id, source.Version))
            .SequenceEqual(sources.Select(source => (source.Id, source.Version))))
        {
            return false;
        }
        if (selectedId != SelectedSourceId)
        {
            Preview = null;
            SearchResult = null;
            WorkflowUrl = null;
            Query = "";
        }
        Sources = sources;
        SelectedSourceId = selectedId;
        if (PendingDelete is { } pending && !sources.Any(source => source.Id == pending))
        {
            PendingDelete = null;
        }
        return true;
    }

    public Task CreateAsync() => PerformAsync(async () =>
    {
        var projectId = ProjectId;
        if (!FieldsApproved)
        {
            throw new ArgumentException("Confirm that the selected fields are approved for this project's readers.");
        }
        if (!CanSave)
        {
            throw new ArgumentException("Complete connection and field approval, then choose a destination queue before saving.");
        }
        var isFeed = ProviderInfo.Kind == "feed";
        var token = isFeed ? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) : null;
        var connection = token is not null ? JsonSerializer.Serialize(new Dictionary<string, string> { ["token"] = token })
            : ConnectionString.Length > 0 ? ConnectionString : JsonSerializer.Serialize(ConnectionFields);
        var source = await dispatcher.Send(new CreateDataSourceCommand(projectId, Name, connection,
            Schema, Table, KeyColumn, Split(Columns), Split(Tags), false, Provider, Queue: QueueConfiguration()));
        if (projectId != ProjectId)
        {
            return;
        }
        SelectedSourceId = source.Id;
        Preview = null;
        SearchResult = null;
        ShowConnect = false;
        FeedToken = token;
        FeedSourceId = token is null ? null : source.Id;
        Notice = isFeed ? "Feed created. Copy its token now and configure your forwarding integration."
            : "Source saved. Run a sync to import the selected fields.";
        ConnectionString = "";
        ConnectionFields = ProviderInfo.Fields.ToDictionary(field => field.Key, field => field.Default);
        Datasets = [];
        await LoadAsync();
    });

    public Task SyncAsync(Guid id, bool rebuild = false) => PerformAsync(async () =>
    {
        var projectId = ProjectId;
        var result = await dispatcher.Send(new SyncDataSourceCommand(projectId, id, rebuild));
        if (projectId != ProjectId)
        {
            return;
        }
        Notice = $"Run complete: {result.Total} records, {result.Changed} updated, {result.Queued} queued, {result.Deleted} deleted, {result.Held} held for review.";
        SearchResult = null;
        Preview = null;
        await LoadAsync();
    });

    public void RequestDelete(Guid id)
    {
        if (!Busy && Sources.Any(source => source.Id == id))
        {
            PendingDelete = id;
        }
    }

    public void CancelDelete()
    {
        if (!Busy)
        {
            PendingDelete = null;
        }
    }

    public Task DeleteAsync(Guid id) => PerformAsync(async () =>
    {
        var projectId = ProjectId;
        await dispatcher.Send(new DeleteDataSourceCommand(projectId, id));
        if (projectId != ProjectId)
        {
            return;
        }
        SearchResult = null;
        Preview = null;
        PendingDelete = null;
        if (FeedSourceId == id)
        {
            FeedToken = null;
            FeedSourceId = null;
        }
        Notice = "Source, credentials and imported records removed. The original database is unchanged.";
        await LoadAsync();
    });

    public Task SearchAsync() => PerformAsync(async () =>
    {
        var projectId = ProjectId;
        var result = await dispatcher.Query(new SearchDataSourceQuery(projectId, SelectedSourceId, Query));
        if (projectId == ProjectId)
        {
            SearchResult = result;
        }
    });

    public Task PreviewAsync(Guid id) => PerformAsync(async () =>
    {
        if (SelectedSourceId != id)
        {
            SelectedSourceId = id;
            Preview = null;
            SearchResult = null;
        }
        var projectId = ProjectId;
        var records = await dispatcher.Query(new ReadSourceRecordsQuery(projectId, id));
        if (projectId == ProjectId && SelectedSourceId == id)
        {
            Preview = records;
        }
    });

    public Task CreateWorkflowAsync(Guid id) => PerformAsync(async () =>
    {
        var projectId = ProjectId;
        var configuredUrl = configuration["PlaceContext:PublicBaseUrl"];
        var hostUrl = string.IsNullOrWhiteSpace(configuredUrl) ? navigation.BaseUri : configuredUrl.Trim();
        var workflow = await dispatcher.Send(new CreateSourceWorkflowCommand(projectId, id, hostUrl));
        if (projectId != ProjectId)
        {
            return;
        }
        WorkflowUrl = $"/project/{ProjectId}/workflows?chain={workflow}";
        Notice = "Workflow created. Open the canvas to add processing steps or schedule runs.";
    });

    private async Task PerformAsync(Func<Task> operation)
    {
        if (Busy)
        {
            return;
        }
        var generation = _generation;
        Busy = true;
        Error = null;
        Notice = null;
        try
        {
            await operation();
        }
        catch (ArgumentException ex) when (generation == _generation)
        {
            Error = ex.Message;
        }
        catch (Exception) when (generation == _generation)
        {
            Error = "The source operation failed. Check permissions, connection settings, source limits and destination queue availability.";
        }
        catch
        {
            // A previous project operation completed after navigation.
        }
        finally
        {
            if (generation == _generation)
            {
                Busy = false;
                NotifyStateChanged();
            }
        }
    }

    private static string[] Split(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
