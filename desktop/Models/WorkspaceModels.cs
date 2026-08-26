using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlaceContext.Desktop.Models;

public partial class NavigationItem : ObservableObject
{
    private readonly Action<NavigationItem> _navigate;

    public NavigationItem(string key, string label, string icon, Action<NavigationItem> navigate)
    {
        Key = key;
        Label = label;
        Icon = icon;
        _navigate = navigate;
        NavigateCommand = new RelayCommand(() => _navigate(this));
    }

    public string Key { get; }
    public string Label { get; }
    public string Icon { get; }
    public IRelayCommand NavigateCommand { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public record MetricCard(string Label, string Value, string Detail, string Accent);
public record ActivityItem(string Title, string Detail, string Time, string Status);
public sealed class PageListItem : ObservableObject
{
    private string _status;

    public PageListItem(
        string title,
        string detail,
        string meta,
        string status,
        string action = "",
        Func<PageListItem, Task>? execute = null)
    {
        Title = title;
        Detail = detail;
        Meta = meta;
        _status = status;
        Action = action;
        ActionCommand = new AsyncRelayCommand(
            () => execute?.Invoke(this) ?? Task.CompletedTask,
            () => execute is not null);
    }

    public string Title { get; }
    public string Detail { get; }
    public string Meta { get; }
    public string Action { get; }
    public bool HasAction => !string.IsNullOrWhiteSpace(Action);
    public IAsyncRelayCommand ActionCommand { get; }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}
public record AgentMember(string Name, string Role, string Initials, string Status, string Accent);
public record GoalItem(string Title, string Owner, int Progress, string Status);
public record ChatChannel(string Name, string Description, int Unread, bool IsActive);
public record ChatMessage(string Author, string Initials, string Time, string Body, string Accent);

public sealed record CoreTenant(bool Resolved, Guid Id, string Slug);
public sealed record DesktopHealthResponse(
    bool Ok,
    string Api,
    CoreTenant Tenant,
    string UserId,
    string Role,
    DateTimeOffset IssuedAt);
public sealed record CoreProject(Guid Id, string Name, string Path, string Status, bool IsGraphified);
public sealed record CoreJob(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    string MapSourceKind,
    string ReturnType,
    bool AllowApiInvocation,
    bool AllowNetworkEgress,
    DateTimeOffset UpdatedAt);
public sealed record CoreJobRun(
    Guid Id,
    Guid JobId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int ShardCount,
    int SucceededShards,
    int PartialShards,
    int FailedShards);
public sealed record CoreResourceItem(
    Guid? Id,
    Guid? ProjectId,
    string Kind,
    string Title,
    string Detail,
    string Meta,
    string Status);
public sealed record DesktopActionResponse(string Status, string Message, Guid? RunId);
public sealed record DesktopQueryResponse(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    int AffectedRows,
    bool Truncated);
public sealed record DesktopChatMessage(string Role, string Content, DateTimeOffset Timestamp);
public sealed record DesktopChatSession(
    Guid Id,
    Guid ProjectId,
    string Title,
    IReadOnlyList<DesktopChatMessage> Messages,
    DateTimeOffset UpdatedAt);
public sealed record ChatLine(string Author, string Content, string Time, bool IsUser);
public sealed record WorkspaceSnapshot(
    IReadOnlyList<CoreProject> Projects,
    IReadOnlyList<CoreJob> Jobs,
    IReadOnlyList<CoreJobRun> Runs,
    IReadOnlyList<CoreResourceItem> Tests,
    IReadOnlyList<CoreResourceItem> Chains,
    IReadOnlyList<CoreResourceItem> Schedules,
    IReadOnlyList<CoreResourceItem> DataResources,
    IReadOnlyList<CoreResourceItem> Secrets,
    IReadOnlyList<CoreResourceItem> Agents,
    IReadOnlyList<CoreResourceItem> AgentChats,
    IReadOnlyList<CoreResourceItem> Artifacts,
    IReadOnlyList<CoreResourceItem> Observability,
    IReadOnlyList<CoreResourceItem> Cluster,
    IReadOnlyList<CoreResourceItem> Wiki);
