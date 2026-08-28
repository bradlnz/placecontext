using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public enum WorkerTarget
{
    StandardWorker,
    AiShard
}

public sealed class ClusterViewModel(
    PlaceContextService service,
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
    public WorkerTarget SelectedWorkerTarget { get; private set; } = WorkerTarget.StandardWorker;
    public int AiShardIndex { get; private set; }
    public int AiShardCount { get; private set; } = 2;
    public bool Copied { get; private set; }
    public double ReadyPercent =>
        Nodes is { Count: > 0 } ? Nodes.Count(node => node.Ready) * 100d / Nodes.Count : 0;
    public string LastSyncLabel => $"Updated {Presentation.Time(DateTimeOffset.Now)}";
    private string? _joinToken;

    public async Task InitializeAsync()
    {
        ui.Set("Cluster", "nodes · local AI · join workers");
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
        _joinToken = null;
        SelectedWorkerTarget = WorkerTarget.StandardWorker;
        AiShardIndex = 0;
        AiShardCount = 2;
        Copied = false;
        ShowAddWorkerOptions = false;
        NotifyStateChanged();
        try
        {
            var token = await service.CreateAgentJoinTokenAsync();
            _joinToken = token;
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
        _joinToken = null;
        ShowAddWorkerOptions = false;
        SelectedWorkerTarget = WorkerTarget.StandardWorker;
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
            WorkerTarget.StandardWorker => "Join a machine that runs normal PlaceContext jobs.",
            WorkerTarget.AiShard => "Join the node, then install its ordered MLX/Torch model shard.",
            _ => "Join a worker node."
        };

    public void SetAiShardIndex(ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out var index))
            AiShardIndex = Math.Clamp(index, 0, AiShardCount - 1);
        Copied = false;
        NotifyStateChanged();
    }

    public void SetAiShardCount(ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out var count))
            AiShardCount = Math.Clamp(count, 1, 32);
        AiShardIndex = Math.Min(AiShardIndex, AiShardCount - 1);
        Copied = false;
        NotifyStateChanged();
    }

    public string? JoinCommand => _joinToken is null
        ? null
        : SelectedWorkerTarget == WorkerTarget.AiShard
            ? BuildAiShardJoinCommand(navigation.BaseUri, _joinToken, AiShardIndex, AiShardCount)
            : BuildJoinCommand(navigation.BaseUri, _joinToken);

    public static string BuildJoinCommand(string baseUri, string token)
    {
        var host = baseUri.TrimEnd('/');
        return $"curl -fsSL {host}/join.sh | bash -s -- --portal {host} --token {token} --node-type standard-worker";
    }

    public static string BuildAiShardJoinCommand(
        string baseUri,
        string token,
        int shardIndex,
        int totalShards)
    {
        var host = baseUri.TrimEnd('/');
        var join = $"curl -fsSL {host}/join.sh | bash -s -- --portal {host} --token {token} --node-type ai-shard";
        var worker = "curl -fsSL https://get.placecontext.io/install.sh"
            + $" | bash -s -- --ai-shard --shard-index {shardIndex} --total-shards {totalShards}";
        return $"{join} && {worker}";
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
