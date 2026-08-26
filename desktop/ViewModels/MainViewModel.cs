using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlaceContext.Desktop.Models;
using PlaceContext.Desktop.Services;

namespace PlaceContext.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly PlaceContextConnectionService _connectionService;
    private readonly EndpointSettingsStore _settingsStore;
    private readonly Dictionary<string, PageViewModel> _pages;
    private OAuthConnection? _activeConnection;

    public MainViewModel(PlaceContextConnectionService connectionService, EndpointSettingsStore settingsStore)
    {
        _connectionService = connectionService;
        _settingsStore = settingsStore;
        _pages = CreatePages();
        PrimaryNavigation = CreateNavigation(("dashboard", "Dashboard", "⌂"), ("projects", "Projects", "▥"), ("jobs", "Jobs", "▶"),
            ("runs", "Runs", "◉"), ("tests", "Tests", "✓"), ("chains", "Chains", "◇"),
            ("schedules", "Schedules", "◷"), ("data", "Data", "▦"), ("vault", "Vault", "◆"));
        TeamNavigation = CreateNavigation(("agents", "Agents", "◎"), ("agent-chat", "Agent chat", "▣"), ("artifacts", "Artifacts", "⬡"));
        SystemNavigation = CreateNavigation(("observability", "Observability", "◌"), ("cluster", "Cluster", "⌘"), ("wiki", "Wiki", "▤"),
            ("settings", "Settings", "⚙"), ("about", "About", "ⓘ"));
        CurrentPage = _pages["dashboard"];
        PrimaryNavigation[0].IsSelected = true;
        _ = LoadSavedEndpointAsync();
    }

    public ObservableCollection<NavigationItem> PrimaryNavigation { get; }
    public ObservableCollection<NavigationItem> TeamNavigation { get; }
    public ObservableCollection<NavigationItem> SystemNavigation { get; }

    [ObservableProperty] public partial string Endpoint { get; set; } = "https://placecontext.lan";
    [ObservableProperty] public partial string? ConnectionError { get; set; }
    [ObservableProperty] public partial string ConnectionStatus { get; set; } = "Connect to your workspace";
    [ObservableProperty] public partial string ConnectedEndpoint { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsConnecting { get; set; }
    [ObservableProperty] public partial bool IsRefreshing { get; set; }
    [ObservableProperty] public partial bool ShowConnection { get; set; } = true;
    [ObservableProperty] public partial bool ShowWorkspace { get; set; }
    [ObservableProperty] public partial PageViewModel CurrentPage { get; set; }
    public string ConnectButtonText => IsConnecting ? "Waiting for sign-in…" : "Sign in with browser";
    public string RefreshButtonText => IsRefreshing ? "Refreshing…" : "Refresh data";
    public bool HasConnectionError => !string.IsNullOrWhiteSpace(ConnectionError);
    partial void OnEndpointChanged(string value) => ConnectCommand.NotifyCanExecuteChanged();
    partial void OnIsConnectingChanged(bool value) => OnPropertyChanged(nameof(ConnectButtonText));
    partial void OnIsRefreshingChanged(bool value)
    {
        OnPropertyChanged(nameof(RefreshButtonText));
        RefreshCommand.NotifyCanExecuteChanged();
    }
    partial void OnConnectionErrorChanged(string? value) => OnPropertyChanged(nameof(HasConnectionError));

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        IsConnecting = true;
        ConnectionError = null;
        ConnectionStatus = "Opening secure browser sign-in…";
        ConnectCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await _connectionService.ConnectAsync(Endpoint);
            Endpoint = result.Endpoint.ToString().TrimEnd('/');
            ConnectedEndpoint = $"{result.Health.Tenant.Slug} · {result.Endpoint.Host}";
            ConnectionStatus = "Loading projects, jobs, and runs…";
            var snapshot = await _connectionService.LoadWorkspaceAsync(result);
            ApplyWorkspace(snapshot, result);
            _activeConnection = result;
            RefreshCommand.NotifyCanExecuteChanged();
            ConnectionStatus = "Connected";
            await _settingsStore.SaveAsync(Endpoint);
            ((SettingsPageViewModel)_pages["settings"]).UpdateConnection(result.Endpoint, result.LatencyMilliseconds);
            ShowConnection = false;
            ShowWorkspace = true;
        }
        catch (Exception exception) when (exception is ArgumentException or HttpRequestException or OperationCanceledException)
        {
            ConnectionStatus = "Connection failed";
            ConnectionError = exception switch
            {
                OperationCanceledException => "OAuth sign-in or the desktop API request timed out.",
                HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized }
                    => $"{exception.Message} The host rejected the token after sign-in. In a multi-service deployment, every desktop API service must use the same PlaceContext:OAuth:SigningKeyPem as the identity service.",
                _ => exception.Message,
            };
        }
        finally
        {
            IsConnecting = false;
            ConnectCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanConnect() => !IsConnecting &&
        !string.IsNullOrWhiteSpace(Endpoint);
    [RelayCommand] private void UseLocal() => Endpoint = "http://localhost:7700";
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (_activeConnection is null) return;
        IsRefreshing = true;
        try
        {
            var snapshot = await _connectionService.LoadWorkspaceAsync(_activeConnection);
            ApplyWorkspace(snapshot, _activeConnection);
            ConnectedEndpoint = $"{_activeConnection.Health.Tenant.Slug} · {_activeConnection.Endpoint.Host} · updated now";
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            ConnectedEndpoint = $"Refresh failed · {exception.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private bool CanRefresh() => _activeConnection is not null && !IsRefreshing;

    [RelayCommand] private void ChangeEndpoint()
    {
        ShowWorkspace = false;
        ShowConnection = true;
        _activeConnection = null;
        RefreshCommand.NotifyCanExecuteChanged();
        ConnectionError = null;
        ConnectionStatus = "Connect to your workspace";
    }

    private async Task LoadSavedEndpointAsync()
    {
        var saved = await _settingsStore.LoadAsync();
        if (saved is null) return;
        if (!string.IsNullOrWhiteSpace(saved.Endpoint)) Endpoint = saved.Endpoint;
    }

    private ObservableCollection<NavigationItem> CreateNavigation(params (string Key, string Label, string Icon)[] values) =>
        new(values.Select(value => new NavigationItem(value.Key, value.Label, value.Icon, Navigate)));

    private void Navigate(NavigationItem selected)
    {
        foreach (var item in PrimaryNavigation.Concat(TeamNavigation).Concat(SystemNavigation)) item.IsSelected = item == selected;
        CurrentPage = _pages[selected.Key];
    }

    private Dictionary<string, PageViewModel> CreatePages() => new()
    {
        ["dashboard"] = new DashboardViewModel(),
        ["settings"] = new SettingsPageViewModel(ChangeEndpoint),
        ["projects"] = LiveCollection("Projects", "Projects returned by /api/desktop/v1/projects"),
        ["jobs"] = new JobsPageViewModel(LoadJobAsync, SaveJobAsync, RunJobAsync),
        ["runs"] = LiveCollection("Runs", "Recent job runs returned by the desktop API"),
        ["tests"] = LiveCollection("Tests", "Native job checks and their latest results"),
        ["chains"] = LiveCollection("Chains", "Native multi-stage job pipelines"),
        ["schedules"] = LiveCollection("Schedules", "Schedules, events, launchpads, and commands"),
        ["data"] = new DataPageViewModel(QueryDataAsync),
        ["vault"] = LiveCollection("Vault", "Encrypted project secret names and status"),
        ["agents"] = LiveCollection("Agents", "Command and worker agents across projects"),
        ["agent-chat"] = new AgentChatPageViewModel(LoadAgentChatAsync, SendAgentMessageAsync),
        ["artifacts"] = LiveCollection("Artifacts", "Files produced by jobs and agents"),
        ["observability"] = LiveCollection("Observability", "Recent job and chain execution activity"),
        ["cluster"] = LiveCollection("Cluster", "Native fleet node inventory"),
        ["wiki"] = LiveCollection("Wiki", "Operator documentation available from the host"),
        ["about"] = LiveCollection("About", "PlaceContext desktop and connected instance information")
    };

    private void ApplyWorkspace(WorkspaceSnapshot snapshot, OAuthConnection connection)
    {
        ((DashboardViewModel)_pages["dashboard"]).Update(snapshot);
        ((CollectionPageViewModel)_pages["projects"]).ReplaceItems(snapshot.Projects.Select(project => new PageListItem(
            project.Name,
            project.Path,
            project.IsGraphified ? "Graph ready" : "Graph pending",
            project.Status)));

        var projects = snapshot.Projects.ToDictionary(project => project.Id);
        ((JobsPageViewModel)_pages["jobs"]).Update(snapshot);

        var jobs = snapshot.Jobs.ToDictionary(job => job.Id);
        ((CollectionPageViewModel)_pages["runs"]).ReplaceItems(snapshot.Runs.Select(run => new PageListItem(
            jobs.TryGetValue(run.JobId, out var job) ? job.Name : $"Job {run.JobId:N}",
            $"{run.SucceededShards}/{run.ShardCount} shards succeeded",
            DashboardViewModel.RelativeTime(run.StartedAt),
            run.Status)));

        ReplaceResources("tests", snapshot.Tests, projects, resource =>
            ("Run", (item, connection) => _connectionService.RunTestAsync(
                connection, resource.ProjectId!.Value, resource.Id!.Value)));
        ReplaceResources("chains", snapshot.Chains, projects, resource =>
            ("Run", (item, connection) => _connectionService.RunChainAsync(
                connection, resource.ProjectId!.Value, resource.Id!.Value)));
        ReplaceResources("schedules", snapshot.Schedules, projects, resource =>
        {
            var enable = !resource.Status.Equals("Enabled", StringComparison.OrdinalIgnoreCase);
            return (enable ? "Enable" : "Disable", (item, connection) =>
                _connectionService.SetScheduleEnabledAsync(
                    connection, resource.ProjectId!.Value, resource.Id!.Value, enable));
        });
        ((DataPageViewModel)_pages["data"]).Update(snapshot);
        ReplaceResources("vault", snapshot.Secrets, projects);
        ReplaceResources("agents", snapshot.Agents, projects);
        ((AgentChatPageViewModel)_pages["agent-chat"]).Update(snapshot);
        ReplaceResources("artifacts", snapshot.Artifacts, projects);
        ReplaceResources("observability", snapshot.Observability, projects);
        ReplaceResources("cluster", snapshot.Cluster, projects);
        ReplaceResources("wiki", snapshot.Wiki, projects);

        ((CollectionPageViewModel)_pages["about"]).ReplaceItems([
            new("Desktop API", connection.Endpoint.ToString().TrimEnd('/'), connection.Health.Role, "Connected"),
            new("Workspace", connection.Health.Tenant.Slug, connection.Health.UserId, "Authenticated")
        ]);
    }

    private static CollectionPageViewModel LiveCollection(string title, string subtitle) =>
        new(title, subtitle, string.Empty, []);

    private void ReplaceResources(
        string page,
        IEnumerable<CoreResourceItem> resources,
        IReadOnlyDictionary<Guid, CoreProject> projects,
        Func<CoreResourceItem, (string Label, Func<PageListItem, OAuthConnection, Task<DesktopActionResponse>> Execute)>? action = null)
    {
        ((CollectionPageViewModel)_pages[page]).ReplaceItems(resources.Select(resource =>
        {
            var project = resource.ProjectId is { } projectId && projects.TryGetValue(projectId, out var value)
                ? value.Name
                : null;
            var meta = project is null ? resource.Meta : $"{project} · {resource.Meta}";
            if (action is null || resource.Id is null || resource.ProjectId is null)
                return new PageListItem(resource.Title, resource.Detail, meta, resource.Status);

            var resourceAction = action(resource);
            return new PageListItem(
                resource.Title,
                resource.Detail,
                meta,
                resource.Status,
                resourceAction.Label,
                item => ExecuteActionAsync(item, connection => resourceAction.Execute(item, connection)));
        }));
    }

    private async Task ExecuteActionAsync(
        PageListItem item,
        Func<OAuthConnection, Task<DesktopActionResponse>> execute)
    {
        if (_activeConnection is null) return;
        var previousStatus = item.Status;
        item.Status = "Working…";
        try
        {
            var result = await execute(_activeConnection);
            item.Status = result.Status;
            ConnectedEndpoint = result.Message;
            await RefreshAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            item.Status = previousStatus;
            ConnectedEndpoint = $"Action failed · {exception.Message}";
        }
    }

    private Task<DesktopQueryResponse> QueryDataAsync(Guid projectId, string sql)
    {
        if (_activeConnection is null)
            throw new HttpRequestException("Connect to a workspace before running a query.");
        return _connectionService.QueryDataAsync(_activeConnection, projectId, sql);
    }

    private Task<DesktopChatSession> LoadAgentChatAsync(Guid projectId, Guid sessionId)
    {
        if (_activeConnection is null)
            throw new HttpRequestException("Connect to a workspace before loading agent chat.");
        return _connectionService.GetAgentChatAsync(_activeConnection, projectId, sessionId);
    }

    private Task<DesktopChatSession> SendAgentMessageAsync(Guid projectId, Guid? sessionId, string message)
    {
        if (_activeConnection is null)
            throw new HttpRequestException("Connect to a workspace before sending a message.");
        return _connectionService.SendAgentMessageAsync(_activeConnection, projectId, sessionId, message);
    }

    private Task<DesktopJobDetail> LoadJobAsync(Guid projectId, Guid jobId)
    {
        if (_activeConnection is null)
            throw new HttpRequestException("Connect to a workspace before loading a job.");
        return _connectionService.GetJobAsync(_activeConnection, projectId, jobId);
    }

    private Task<DesktopJobDetail> SaveJobAsync(DesktopJobDetail job, bool create)
    {
        if (_activeConnection is null)
            throw new HttpRequestException("Connect to a workspace before saving a job.");
        return create
            ? _connectionService.CreateJobAsync(_activeConnection, job.ProjectId, job)
            : _connectionService.UpdateJobAsync(_activeConnection, job);
    }

    private Task<DesktopActionResponse> RunJobAsync(Guid projectId, Guid jobId, string? inputPayload)
    {
        if (_activeConnection is null)
            throw new HttpRequestException("Connect to a workspace before running a job.");
        return _connectionService.RunJobAsync(_activeConnection, projectId, jobId, inputPayload);
    }

    private static CollectionPageViewModel Unavailable(string title, string subtitle) =>
        new(title, subtitle, string.Empty, [],
            $"{title} is not exposed by the current desktop API. No preview records are shown.");
}
