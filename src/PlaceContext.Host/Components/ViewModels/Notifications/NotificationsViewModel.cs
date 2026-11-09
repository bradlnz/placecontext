using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Operations;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Access;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class NotificationsViewModel : PageViewModel, IComponentViewModel, IDisposable
{
    private readonly OperationCenter _operationsCenter;
    private readonly ICurrentTenant _tenant;
    private readonly NavigationManager _navigation;
    private bool _initialized;
    private IDisposable? _operationSubscription;
    private readonly ICurrentUser? _user;
    private readonly IPermissionService? _permissions;
    private bool _canViewWorkflowInputs;

    public NotificationsViewModel(
        OperationCenter operationsCenter,
        ICurrentTenant tenant,
        NavigationManager navigation,
        ICurrentUser? user = null,
        IPermissionService? permissions = null
    )
    {
        _operationsCenter = operationsCenter;
        _tenant = tenant;
        _navigation = navigation;
        _user = user;
        _permissions = permissions;
    }

    public bool IsOpen
    {
        get; private set;
    }
    public IReadOnlyList<PortalOperation> Operations { get; private set; } = [];
    public int ActiveCount
    {
        get; private set;
    }
    public int NeedsInputCount
    {
        get; private set;
    }
    public string ActiveSummary => NeedsInputCount > 0
        ? $"{NeedsInputCount} need input" + (ActiveCount > 0 ? $" · {ActiveCount} in progress" : "")
        : ActiveCount > 0 ? $"{ActiveCount} in progress" : "all quiet";

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }
        if (_tenant.IsResolved)
        {
            _operationSubscription = _operationsCenter.Subscribe(_tenant.TenantId, OnOperationsChanged, _user?.UserId);
        }
        _initialized = true;
        Reload();
    }

    public async Task InitializeAsync()
    {
        Initialize();
        if (_permissions is not null)
        {
            try
            {
                _canViewWorkflowInputs = await _permissions.HasAsync(Permission.ChainsManage)
                    && await _permissions.HasAsync(Permission.JobsRun);
            }
            catch
            {
                _canViewWorkflowInputs = false;
            }
        }
        Reload();
        NotifyStateChanged();
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;
        Reload();
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
        NotifyStateChanged();
        _navigation.NavigateTo(link);
    }

    public static string StatusLine(PortalOperation operation) =>
        operation.Status switch
        {
            PortalOperationStatus.Queued =>
                $"queued {operation.QueuedAt.ToWorkspaceTime():HH:mm:ss}",
            PortalOperationStatus.Running =>
                $"running for {Elapsed(operation.StartedAt ?? operation.QueuedAt)}",
            PortalOperationStatus.NeedsInput =>
                "Awaiting input · workflow paused",
            PortalOperationStatus.Succeeded =>
                $"finished {operation.FinishedAt?.ToWorkspaceTime():HH:mm:ss} in {Duration(operation)}",
            PortalOperationStatus.Failed =>
                $"failed {operation.FinishedAt?.ToWorkspaceTime():HH:mm:ss} after {Duration(operation)}",
            _ => string.Empty,
        };

    public string StatusIcon(PortalOperationStatus status) =>
        status switch
        {
            PortalOperationStatus.Queued => "clock",
            PortalOperationStatus.Running => "refresh",
            PortalOperationStatus.NeedsInput => "human",
            PortalOperationStatus.Succeeded => "check",
            PortalOperationStatus.Failed => "close",
            _ => "about",
        };

    public string StatusClass(PortalOperationStatus status) =>
        status switch
        {
            PortalOperationStatus.Queued => "i-muted",
            PortalOperationStatus.Running => "op-spin",
            PortalOperationStatus.NeedsInput => "i-attention",
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
            _operationSubscription?.Dispose();
            _initialized = false;
        }

        Detach();
    }

    private void OnOperationsChanged()
    {
        var previous = ActiveCount;
        var previousInput = NeedsInputCount;
        Reload();
        if (IsOpen || ActiveCount != previous || NeedsInputCount != previousInput)
        {
            NotifyStateChanged();
        }
    }

    private void Reload()
    {
        ActiveCount = _tenant.IsResolved ? _operationsCenter.ActiveCount(_tenant.TenantId, _user?.UserId) : 0;
        NeedsInputCount = _tenant.IsResolved && _canViewWorkflowInputs
            ? _operationsCenter.NeedsInputCount(_tenant.TenantId, _user?.UserId) : 0;
        Operations = IsOpen && _tenant.IsResolved
            ? _operationsCenter.ListForTenant(_tenant.TenantId, _user?.UserId)
                .Where(o => _canViewWorkflowInputs || o.Status != PortalOperationStatus.NeedsInput).ToArray() : [];
    }

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
