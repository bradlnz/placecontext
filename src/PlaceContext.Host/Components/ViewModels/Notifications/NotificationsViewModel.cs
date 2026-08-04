using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class NotificationsViewModel : PageViewModel, IComponentViewModel, IDisposable
{
    private readonly OperationCenter _operationsCenter;
    private readonly ICurrentTenant _tenant;
    private readonly NavigationManager _navigation;
    private bool _initialized;

    public NotificationsViewModel(
        OperationCenter operationsCenter,
        ICurrentTenant tenant,
        NavigationManager navigation
    )
    {
        _operationsCenter = operationsCenter;
        _tenant = tenant;
        _navigation = navigation;
    }

    public bool IsOpen { get; private set; }
    public IReadOnlyList<PortalOperation> Operations { get; private set; } = [];
    public int ActiveCount =>
        _tenant.IsResolved ? _operationsCenter.ActiveCount(_tenant.TenantId) : 0;
    public string ActiveSummary => ActiveCount > 0 ? $"{ActiveCount} in progress" : "all quiet";

    public void Initialize()
    {
        if (_initialized)
            return;

        _operationsCenter.Changed += OnOperationsChanged;
        _initialized = true;
        Reload();
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;
        NotifyStateChanged();
    }

    public void Close()
    {
        IsOpen = false;
        NotifyStateChanged();
    }

    public void Open(string link)
    {
        IsOpen = false;
        _navigation.NavigateTo(link);
    }

    public static string StatusLine(PortalOperation operation) =>
        operation.Status switch
        {
            PortalOperationStatus.Queued =>
                $"queued {operation.QueuedAt.ToWorkspaceTime():HH:mm:ss}",
            PortalOperationStatus.Running =>
                $"running for {Elapsed(operation.StartedAt ?? operation.QueuedAt)}",
            PortalOperationStatus.Succeeded =>
                $"finished {operation.FinishedAt?.ToWorkspaceTime():HH:mm:ss} in {Duration(operation)}",
            PortalOperationStatus.Failed =>
                $"failed {operation.FinishedAt?.ToWorkspaceTime():HH:mm:ss} after {Duration(operation)}",
            _ => string.Empty,
        };

    public string StatusIcon(PortalOperationStatus status) =>
        status switch
        {
            PortalOperationStatus.Queued => "⏳",
            PortalOperationStatus.Running => "◐",
            PortalOperationStatus.Succeeded => "✓",
            PortalOperationStatus.Failed => "✗",
            _ => "?",
        };

    public string StatusClass(PortalOperationStatus status) =>
        status switch
        {
            PortalOperationStatus.Queued => "i-muted",
            PortalOperationStatus.Running => "op-spin",
            PortalOperationStatus.Succeeded => "i-good",
            PortalOperationStatus.Failed => "i-bad",
            _ => string.Empty,
        };

    public string OutcomeStyle(PortalOperationStatus status) =>
        status == PortalOperationStatus.Failed ? "var(--bad)" : "var(--text-3)";

    public void Dispose()
    {
        if (_initialized)
        {
            _operationsCenter.Changed -= OnOperationsChanged;
            _initialized = false;
        }

        Detach();
    }

    private void OnOperationsChanged()
    {
        Reload();
        NotifyStateChanged();
    }

    private void Reload() =>
        Operations = _tenant.IsResolved ? _operationsCenter.ListForTenant(_tenant.TenantId) : [];

    private static string Elapsed(DateTimeOffset since) =>
        FormatDuration(DateTimeOffset.UtcNow - since);

    private static string Duration(PortalOperation operation) =>
        operation.StartedAt is { } started && operation.FinishedAt is { } finished
            ? FormatDuration(finished - started)
            : "—";

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
        : duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
        : $"{Math.Max(0, duration.Seconds)}s";
}
