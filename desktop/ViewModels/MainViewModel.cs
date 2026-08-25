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
            ConnectionError = exception is OperationCanceledException
                ? "OAuth sign-in or the desktop API request timed out."
                : exception.Message;
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
        ["jobs"] = LiveCollection("Jobs", "Jobs returned across connected projects"),
        ["runs"] = LiveCollection("Runs", "Recent job runs returned by the desktop API"),
        ["tests"] = Unavailable("Tests", "Define checks and inspect their latest runs"),
        ["chains"] = Unavailable("Chains", "Compose repeatable multi-step workflows"),
        ["schedules"] = Unavailable("Schedules", "Automate jobs and chains"),
        ["data"] = Unavailable("Data", "Explore workspace datasets and connections"),
        ["vault"] = Unavailable("Vault", "Manage secrets without exposing values"),
        ["agents"] = Unavailable("Agents", "Build teams of agents around shared work and goals"),
        ["agent-chat"] = Unavailable("Agent chat", "Shared channels for your agent teams"),
        ["artifacts"] = Unavailable("Artifacts", "Outputs shared by agents and jobs"),
        ["observability"] = Unavailable("Observability", "Health, traces, and runtime signals"),
        ["cluster"] = Unavailable("Cluster", "Nodes and services in this PlaceContext instance"),
        ["wiki"] = Unavailable("Wiki", "Workspace knowledge for people and agents"),
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
        ((CollectionPageViewModel)_pages["jobs"]).ReplaceItems(snapshot.Jobs.Select(job => new PageListItem(
            job.Name,
            job.Description ?? $"{job.MapSourceKind} workload",
            projects.TryGetValue(job.ProjectId, out var project) ? project.Name : job.ProjectId.ToString("N"),
            job.ReturnType)));

        var jobs = snapshot.Jobs.ToDictionary(job => job.Id);
        ((CollectionPageViewModel)_pages["runs"]).ReplaceItems(snapshot.Runs.Select(run => new PageListItem(
            jobs.TryGetValue(run.JobId, out var job) ? job.Name : $"Job {run.JobId:N}",
            $"{run.SucceededShards}/{run.ShardCount} shards succeeded",
            DashboardViewModel.RelativeTime(run.StartedAt),
            run.Status)));

        ((CollectionPageViewModel)_pages["about"]).ReplaceItems([
            new("Desktop API", connection.Endpoint.ToString().TrimEnd('/'), connection.Health.Role, "Connected"),
            new("Workspace", connection.Health.Tenant.Slug, connection.Health.UserId, "Authenticated")
        ]);
    }

    private static CollectionPageViewModel LiveCollection(string title, string subtitle) =>
        new(title, subtitle, string.Empty, []);

    private static CollectionPageViewModel Unavailable(string title, string subtitle) =>
        new(title, subtitle, string.Empty, [],
            $"{title} is not exposed by the current desktop API. No preview records are shown.");
}
