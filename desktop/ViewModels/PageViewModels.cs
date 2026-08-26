using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlaceContext.Desktop.Models;

namespace PlaceContext.Desktop.ViewModels;

public abstract class PageViewModel(string title, string subtitle) : ViewModelBase
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
}

public partial class DashboardViewModel : PageViewModel
{
    public DashboardViewModel() : base("Dashboard", "Your PlaceContext workspace at a glance") { }
    public ObservableCollection<MetricCard> Metrics { get; } = [];
    public ObservableCollection<ActivityItem> Activity { get; } = [];
    [ObservableProperty] public partial string HealthSummary { get; set; } = "Waiting for desktop API data";
    [ObservableProperty] public partial string ProjectStatus { get; set; } = "—";
    [ObservableProperty] public partial string JobStatus { get; set; } = "—";
    [ObservableProperty] public partial string RunStatus { get; set; } = "—";

    public void Update(WorkspaceSnapshot snapshot)
    {
        Metrics.Clear();
        Metrics.Add(new("PROJECTS", snapshot.Projects.Count.ToString(), "Available through the desktop API", "#46DF7B"));
        Metrics.Add(new("JOBS", snapshot.Jobs.Count.ToString(), "Across all projects", "#63A9FF"));
        Metrics.Add(new("ACTIVE RUNS", snapshot.Runs.Count(run => IsActive(run.Status)).ToString(), "Queued or currently running", "#A78BFA"));
        Metrics.Add(new("RECENT RUNS", snapshot.Runs.Count.ToString(), "Latest runs returned per job", "#F4B860"));
        HealthSummary = "Desktop API data loaded";
        ProjectStatus = $"{snapshot.Projects.Count} returned";
        JobStatus = $"{snapshot.Jobs.Count} returned";
        RunStatus = $"{snapshot.Runs.Count(run => IsActive(run.Status))} active";

        var jobs = snapshot.Jobs.ToDictionary(job => job.Id);
        Activity.Clear();
        foreach (var run in snapshot.Runs.Take(8))
        {
            var title = jobs.TryGetValue(run.JobId, out var job) ? job.Name : $"Job {run.JobId:N}";
            var detail = $"{run.SucceededShards}/{run.ShardCount} shards succeeded";
            Activity.Add(new(title, detail, RelativeTime(run.StartedAt), run.Status));
        }
    }

    private static bool IsActive(string status) =>
        status.Equals("Running", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("Queued", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("Pending", StringComparison.OrdinalIgnoreCase);

    internal static string RelativeTime(DateTimeOffset timestamp)
    {
        var elapsed = DateTimeOffset.UtcNow - timestamp;
        if (elapsed.TotalMinutes < 1) return "just now";
        if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours}h ago";
        return $"{(int)elapsed.TotalDays}d ago";
    }
}

public partial class CollectionPageViewModel : PageViewModel
{
    public CollectionPageViewModel(string title, string subtitle, string action, IEnumerable<PageListItem> items, string? emptyMessage = null)
        : base(title, subtitle)
    {
        Action = action;
        EmptyMessage = emptyMessage ?? $"No {title.ToLowerInvariant()} were returned by the desktop API.";
        Items = new ObservableCollection<PageListItem>(items);
        HasItems = Items.Count > 0;
    }

    public string Action { get; }
    public bool HasAction => !string.IsNullOrWhiteSpace(Action);
    public string EmptyMessage { get; }
    public ObservableCollection<PageListItem> Items { get; }
    [ObservableProperty] public partial bool HasItems { get; set; }

    public void ReplaceItems(IEnumerable<PageListItem> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        HasItems = Items.Count > 0;
    }
}

public partial class DataPageViewModel : PageViewModel
{
    private readonly Func<Guid, string, Task<DesktopQueryResponse>> _query;

    public DataPageViewModel(Func<Guid, string, Task<DesktopQueryResponse>> query)
        : base("Data", "Explore project tables and run read-only SQL")
    {
        _query = query;
    }

    public ObservableCollection<CoreProject> Projects { get; } = [];
    public ObservableCollection<PageListItem> Resources { get; } = [];
    public ObservableCollection<string> Results { get; } = [];
    [ObservableProperty] public partial CoreProject? SelectedProject { get; set; }
    [ObservableProperty] public partial string Sql { get; set; } = "SELECT * FROM job_runs LIMIT 50";
    [ObservableProperty] public partial string ResultSummary { get; set; } = "Choose a project and run a SELECT statement.";
    [ObservableProperty] public partial bool IsRunning { get; set; }
    public bool HasResources => Resources.Count > 0;
    public bool HasResults => Results.Count > 0;

    partial void OnSelectedProjectChanged(CoreProject? value) => RunQueryCommand.NotifyCanExecuteChanged();
    partial void OnSqlChanged(string value) => RunQueryCommand.NotifyCanExecuteChanged();
    partial void OnIsRunningChanged(bool value) => RunQueryCommand.NotifyCanExecuteChanged();

    public void Update(WorkspaceSnapshot snapshot)
    {
        var selectedId = SelectedProject?.Id;
        Projects.Clear();
        foreach (var project in snapshot.Projects) Projects.Add(project);
        SelectedProject = Projects.FirstOrDefault(project => project.Id == selectedId) ?? Projects.FirstOrDefault();

        Resources.Clear();
        var projects = snapshot.Projects.ToDictionary(project => project.Id);
        foreach (var resource in snapshot.DataResources)
        {
            var projectName = resource.ProjectId is { } id && projects.TryGetValue(id, out var project)
                ? project.Name
                : resource.Meta;
            Resources.Add(new PageListItem(resource.Title, resource.Detail, projectName, resource.Status));
        }
        OnPropertyChanged(nameof(HasResources));
    }

    [RelayCommand(CanExecute = nameof(CanRunQuery))]
    private async Task RunQueryAsync()
    {
        if (SelectedProject is null) return;
        IsRunning = true;
        Results.Clear();
        OnPropertyChanged(nameof(HasResults));
        try
        {
            var result = await _query(SelectedProject.Id, Sql);
            Results.Add(string.Join("  │  ", result.Columns));
            foreach (var row in result.Rows)
                Results.Add(string.Join("  │  ", row.Select(value => value ?? "NULL")));
            ResultSummary = result.Rows.Count == 0
                ? "Query completed with no rows."
                : $"{result.Rows.Count:N0} rows returned{(result.Truncated ? " (limited)" : string.Empty)}.";
            OnPropertyChanged(nameof(HasResults));
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            ResultSummary = $"Query failed · {exception.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanRunQuery() => !IsRunning && SelectedProject is not null && !string.IsNullOrWhiteSpace(Sql);
}

public partial class AgentChatPageViewModel : PageViewModel
{
    private readonly Func<Guid, Guid, Task<DesktopChatSession>> _load;
    private readonly Func<Guid, Guid?, string, Task<DesktopChatSession>> _send;

    public AgentChatPageViewModel(
        Func<Guid, Guid, Task<DesktopChatSession>> load,
        Func<Guid, Guid?, string, Task<DesktopChatSession>> send)
        : base("Agent chat", "Talk to your workspace agents in a native conversation")
    {
        _load = load;
        _send = send;
    }

    public ObservableCollection<CoreProject> Projects { get; } = [];
    public ObservableCollection<CoreResourceItem> Sessions { get; } = [];
    public ObservableCollection<ChatLine> Messages { get; } = [];
    [ObservableProperty] public partial CoreProject? SelectedProject { get; set; }
    [ObservableProperty] public partial CoreResourceItem? SelectedSession { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = string.Empty;
    [ObservableProperty] public partial string ConversationTitle { get; set; } = "New conversation";
    [ObservableProperty] public partial string Status { get; set; } = "Choose a session or start a new conversation.";
    [ObservableProperty] public partial bool IsWorking { get; set; }

    partial void OnSelectedProjectChanged(CoreProject? value)
    {
        FilterSessions();
        SendCommand.NotifyCanExecuteChanged();
    }
    partial void OnSelectedSessionChanged(CoreResourceItem? value)
    {
        if (value?.Id is { } id && value.ProjectId is { } projectId)
            _ = LoadAsync(projectId, id);
    }
    partial void OnMessageChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnIsWorkingChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

    private IReadOnlyList<CoreResourceItem> AllSessions { get; set; } = [];

    public void Update(WorkspaceSnapshot snapshot)
    {
        var selectedProjectId = SelectedProject?.Id;
        Projects.Clear();
        foreach (var project in snapshot.Projects) Projects.Add(project);
        AllSessions = snapshot.AgentChats;
        SelectedProject = Projects.FirstOrDefault(project => project.Id == selectedProjectId) ?? Projects.FirstOrDefault();
        FilterSessions();
    }

    [RelayCommand]
    private void NewConversation()
    {
        SelectedSession = null;
        Messages.Clear();
        ConversationTitle = "New conversation";
        Status = "Write a message to start a new agent session.";
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (SelectedProject is null) return;
        var content = Message.Trim();
        var sessionId = SelectedSession?.Id;
        Message = string.Empty;
        IsWorking = true;
        Status = "Agent is working…";
        try
        {
            var session = await _send(SelectedProject.Id, sessionId, content);
            ApplySession(session);
            Status = "Reply received.";
            if (Sessions.All(value => value.Id != session.Id))
            {
                var item = new CoreResourceItem(session.Id, session.ProjectId, "chat", session.Title,
                    $"{session.Messages.Count} messages", session.UpdatedAt.ToLocalTime().ToString("g"), "Session");
                Sessions.Insert(0, item);
                SelectedSession = item;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            Message = content;
            Status = $"Message failed · {exception.Message}";
        }
        finally
        {
            IsWorking = false;
        }
    }

    private bool CanSend() => !IsWorking && SelectedProject is not null && !string.IsNullOrWhiteSpace(Message);

    private async Task LoadAsync(Guid projectId, Guid sessionId)
    {
        IsWorking = true;
        Status = "Loading conversation…";
        try
        {
            ApplySession(await _load(projectId, sessionId));
            Status = "Conversation loaded.";
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            Status = $"Conversation failed to load · {exception.Message}";
        }
        finally
        {
            IsWorking = false;
        }
    }

    private void ApplySession(DesktopChatSession session)
    {
        ConversationTitle = session.Title;
        Messages.Clear();
        foreach (var message in session.Messages)
            Messages.Add(new ChatLine(
                message.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "You" : "Agent",
                message.Content,
                message.Timestamp.ToLocalTime().ToString("t"),
                message.Role.Equals("user", StringComparison.OrdinalIgnoreCase)));
    }

    private void FilterSessions()
    {
        var selectedId = SelectedSession?.Id;
        Sessions.Clear();
        if (SelectedProject is { } project)
            foreach (var session in AllSessions.Where(value => value.ProjectId == project.Id)) Sessions.Add(session);
        SelectedSession = Sessions.FirstOrDefault(value => value.Id == selectedId);
    }
}

public sealed class AgentsPageViewModel : PageViewModel
{
    public AgentsPageViewModel() : base("Agents", "Build teams of agents around shared work and goals") { }
    public IReadOnlyList<AgentMember> Members { get; } =
    [
        new("Orchestrator", "Team lead", "OR", "Working", "#46DF7B"), new("Research", "Research specialist", "RE", "Online", "#63A9FF"),
        new("Builder", "Implementation specialist", "BU", "Working", "#A78BFA"), new("Reviewer", "Quality and review", "RV", "Online", "#F4B860")
    ];
    public IReadOnlyList<GoalItem> Goals { get; } =
    [
        new("Ship native desktop client", "Builder", 68, "In progress"), new("Complete launch readiness review", "Reviewer", 42, "In progress"),
        new("Map customer onboarding gaps", "Research", 90, "Review")
    ];
}

public sealed class AgentChatViewModel : PageViewModel
{
    public AgentChatViewModel() : base("Agent chat", "Shared channels for your agent teams") { }
    public IReadOnlyList<ChatChannel> Channels { get; } =
    [new("launch-team", "Product launch coordination", 3, true), new("engineering", "Build and review", 0, false), new("research", "Sources and findings", 1, false)];
    public IReadOnlyList<ChatMessage> Messages { get; } =
    [
        new("Orchestrator", "OR", "09:42", "The desktop client work is now the highest-priority team goal. Builder has the native shell underway.", "#46DF7B"),
        new("Research", "RE", "09:45", "I mapped the current screens and grouped the backend contracts needed for the first connection pass.", "#63A9FF"),
        new("Builder", "BU", "09:49", "Native endpoint onboarding and navigation are ready. I’m finishing the agents and goals views now.", "#A78BFA"),
        new("Reviewer", "RV", "09:51", "I’ll validate keyboard navigation, empty states, and connection failure handling when the build lands.", "#F4B860")
    ];
    public IReadOnlyList<AgentMember> Team { get; } =
    [
        new("Orchestrator", "Team lead", "OR", "Working", "#46DF7B"), new("Research", "Research", "RE", "Online", "#63A9FF"),
        new("Builder", "Engineering", "BU", "Working", "#A78BFA"), new("Reviewer", "Quality", "RV", "Online", "#F4B860")
    ];
}

public partial class SettingsPageViewModel : PageViewModel
{
    private readonly Action _reconnect;

    public SettingsPageViewModel(Action reconnect) : base("Settings", "Manage this desktop client and your PlaceContext workspace")
    {
        _reconnect = reconnect;
    }

    [ObservableProperty] public partial string ConnectedEndpoint { get; set; } = "Not connected";
    [ObservableProperty] public partial string ConnectionDetail { get; set; } = "Connect an instance to see its status.";
    public string ClientVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
    public string RuntimeVersion { get; } = $".NET {Environment.Version}";
    public string Platform { get; } = System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    public void UpdateConnection(Uri endpoint, long latencyMilliseconds)
    {
        ConnectedEndpoint = endpoint.ToString().TrimEnd('/');
        ConnectionDetail = $"OAuth session active · {latencyMilliseconds} ms response";
    }

    [RelayCommand]
    private void Reconnect() => _reconnect();
}
