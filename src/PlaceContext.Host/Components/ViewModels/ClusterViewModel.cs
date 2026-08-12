using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public enum WorkerTarget
{
    Server,
    Mac
}

public sealed class ClusterViewModel(
    IPlaceContextService service,
    PortalUiState ui,
    NavigationManager navigation,
    IJSRuntime js
) : PageViewModel
{
    private const string LocalEnvironment = "Local environment";
    public bool Loading { get; private set; } = true;
    public bool Busy { get; private set; }
    public string? Message { get; private set; }
    public bool MessageOk { get; private set; }
    public IReadOnlyList<ClusterNode>? Nodes { get; private set; }
    public ClusterInfo? Cluster { get; private set; }
    public bool ShowAddWorkerOptions { get; private set; }
    public WorkerTarget SelectedWorkerTarget { get; private set; } = WorkerTarget.Server;
    public bool Copied { get; private set; }
    public double ReadyPercent =>
        Nodes is { Count: > 0 } ? Nodes.Count(node => node.Ready) * 100d / Nodes.Count : 0;
    public string LastSyncLabel => $"Updated {Presentation.Time(DateTimeOffset.Now)}";
    private string? _serverJoinCommand;
    private string? _macJoinCommand;

    public async Task InitializeAsync()
    {
        ui.Set("Cluster", "nodes · agents · join workers");
        await RefreshAsync();
    }

    public async Task RefreshAsync(bool showLoading = true)
    {
        Loading = showLoading;
        Busy = !showLoading;
        Message = null;
        try
        {
            Cluster = await service.GetClusterInfoAsync();
            Nodes = Cluster.Nodes.ToList();
            MessageOk = true;
        }
        catch (Exception ex)
        {
            Message = $"Failed to load cluster info: {ex.Message}";
            MessageOk = false;
        }
        finally
        {
            Loading = false;
            Busy = false;
            NotifyStateChanged();
        }
    }

    public async Task AddWorkerAsync()
    {
        Busy = true;
        Message = null;
        _serverJoinCommand = null;
        _macJoinCommand = null;
        SelectedWorkerTarget = WorkerTarget.Server;
        Copied = false;
        ShowAddWorkerOptions = false;
        NotifyStateChanged();
        try
        {
            var token = await service.CreateAgentJoinTokenAsync();
            var command = BuildJoinCommand(navigation.BaseUri, token);
            _serverJoinCommand = command;
            _macJoinCommand = command;
            ShowAddWorkerOptions = true;
            MessageOk = true;
        }
        catch (Exception ex)
        {
            Message = $"Failed to create join token: {ex.Message}";
            MessageOk = false;
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }

    public async Task CopyCommandAsync()
    {
        if (JoinCommand is null)
            return;
        await js.InvokeVoidAsync("navigator.clipboard.writeText", JoinCommand);
        Copied = true;
        NotifyStateChanged();
    }

    public void DismissJoin()
    {
        _serverJoinCommand = null;
        _macJoinCommand = null;
        ShowAddWorkerOptions = false;
        SelectedWorkerTarget = WorkerTarget.Server;
        Copied = false;
        NotifyStateChanged();
    }

    public void SelectWorkerTarget(WorkerTarget target)
    {
        SelectedWorkerTarget = target;
        Copied = false;
        NotifyStateChanged();
    }

    public string? WorkerDescription(WorkerTarget target) =>
        target switch
        {
            WorkerTarget.Server => "Join a Linux/server machine with k3s and Docker.",
            WorkerTarget.Mac => "Join a Mac workstation via Docker worker mode.",
            _ => "Join a worker node."
        };

    private string? ActiveJoinCommand => SelectedWorkerTarget switch
    {
        WorkerTarget.Server => _serverJoinCommand,
        WorkerTarget.Mac => _macJoinCommand,
        _ => _serverJoinCommand
    };

    public string? JoinCommand => ActiveJoinCommand;

    public static string BuildJoinCommand(string baseUri, string token)
    {
        var host = baseUri.TrimEnd('/');
        return $"curl -fsSL {host}/join.sh | bash -s -- --portal {host} --token {token}";
    }

    public static string PlatformLabel(ClusterNode node) =>
        $"{(string.IsNullOrWhiteSpace(node.OperatingSystem) ? "Unknown OS" : node.OperatingSystem)} · {node.Architecture}";

    public static string RelativeAge(DateTimeOffset? createdAt)
    {
        if (createdAt is null)
            return "Local";
        var age = DateTimeOffset.UtcNow - createdAt.Value;
        if (age.TotalMinutes < 2)
            return "Just now";
        if (age.TotalHours < 1)
            return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalDays < 1)
            return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays}d ago";
    }
}
