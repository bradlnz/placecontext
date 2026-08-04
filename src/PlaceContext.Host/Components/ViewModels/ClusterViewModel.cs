using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

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
    public string? JoinCommand { get; private set; }
    public bool Copied { get; private set; }
    public double ReadyPercent =>
        Nodes is { Count: > 0 } ? Nodes.Count(node => node.Ready) * 100d / Nodes.Count : 0;
    public string LastSyncLabel => $"Updated {Presentation.Time(DateTimeOffset.Now)}";

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
        JoinCommand = null;
        Copied = false;
        try
        {
            var token = await service.CreateAgentJoinTokenAsync();
            JoinCommand = BuildJoinCommand(navigation.BaseUri, token);
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
        JoinCommand = null;
        Copied = false;
    }

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
