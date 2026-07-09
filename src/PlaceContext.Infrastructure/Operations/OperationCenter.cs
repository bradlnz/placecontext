using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Operations;

public enum PortalOperationStatus { Queued, Running, Succeeded, Failed }

/// <summary>One background portal operation, shown in the notifications pane.</summary>
public sealed class PortalOperation
{
    public Guid Id { get; } = Guid.NewGuid();
    public required Guid TenantId { get; init; }
    public Guid? ProjectId { get; init; }
    public required string Title { get; init; }
    /// <summary>Where to go to see the result (e.g. the job's run history).</summary>
    public string? Link { get; init; }
    public PortalOperationStatus Status { get; internal set; } = PortalOperationStatus.Queued;
    public DateTimeOffset QueuedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; internal set; }
    public DateTimeOffset? FinishedAt { get; internal set; }
    /// <summary>Short human outcome ("Run abc123 — Succeeded") or the failure message.</summary>
    public string? Outcome { get; internal set; }
}

/// <summary>
/// The portal's background-operation ledger — the Azure-notifications-pane pattern. Pages enqueue
/// work here instead of awaiting it in the circuit, so an operation survives navigation (and the
/// circuit itself); the bell in the shell header shows progress from any page. Operations are
/// in-memory per replica: the pane is a live view of "what this portal is doing right now", not an
/// audit log — durable history belongs to the artifacts the operations produce (runs, charts, …).
/// Work runs with the requesting tenant re-applied and its own DI scope.
/// </summary>
public sealed class OperationCenter
{
    private const int KeepPerTenant = 50;

    private readonly IServiceScopeFactory _scopes;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<OperationCenter>? _log;
    private readonly object _gate = new();
    private readonly List<PortalOperation> _ops = new();

    public OperationCenter(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime,
        ILogger<OperationCenter>? log = null)
    {
        _scopes = scopes;
        _lifetime = lifetime;
        _log = log;
    }

    /// <summary>Raised on every status change; the bell + pane re-render on it.</summary>
    public event Action? Changed;

    public IReadOnlyList<PortalOperation> ListForTenant(Guid tenantId)
    {
        lock (_gate)
            return _ops.Where(o => o.TenantId == tenantId).OrderByDescending(o => o.QueuedAt).ToList();
    }

    public int ActiveCount(Guid tenantId)
    {
        lock (_gate)
            return _ops.Count(o => o.TenantId == tenantId
                && o.Status is PortalOperationStatus.Queued or PortalOperationStatus.Running);
    }

    /// <summary>
    /// Run <paramref name="work"/> in the background under the tenant's context and its own scope.
    /// Returns immediately; the returned string (if any) becomes the operation's outcome line.
    /// </summary>
    public PortalOperation Run(TenantInfo tenant, Guid? projectId, string title, string? link,
        Func<IServiceProvider, CancellationToken, Task<string?>> work)
    {
        var op = Track(tenant, projectId, title, link);
        _ = Task.Run(async () =>
        {
            MarkRunning(op.Id);
            CurrentTenant.Set(tenant);
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var outcome = await work(scope.ServiceProvider, _lifetime.ApplicationStopping);
                MarkDone(op.Id, outcome);
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Portal operation failed: {Title}", title);
                MarkFailed(op.Id, ex.Message);
            }
            finally { CurrentTenant.Clear(); }
        });
        return op;
    }

    /// <summary>Register an operation whose lifecycle an external worker drives (e.g. the analytics sweep).</summary>
    public PortalOperation Track(TenantInfo tenant, Guid? projectId, string title, string? link)
    {
        var op = new PortalOperation { TenantId = tenant.Id, ProjectId = projectId, Title = title, Link = link };
        lock (_gate)
        {
            _ops.Add(op);
            var tenantOps = _ops.Where(o => o.TenantId == tenant.Id).OrderByDescending(o => o.QueuedAt).ToList();
            foreach (var stale in tenantOps.Skip(KeepPerTenant)
                         .Where(o => o.Status is PortalOperationStatus.Succeeded or PortalOperationStatus.Failed))
                _ops.Remove(stale);
        }
        Notify();
        return op;
    }

    public void MarkRunning(Guid opId) => Update(opId, o => { o.Status = PortalOperationStatus.Running; o.StartedAt = DateTimeOffset.UtcNow; });

    public void MarkDone(Guid opId, string? outcome = null)
        => Update(opId, o => { o.Status = PortalOperationStatus.Succeeded; o.FinishedAt = DateTimeOffset.UtcNow; o.Outcome = outcome; });

    public void MarkFailed(Guid opId, string error)
        => Update(opId, o =>
        {
            o.Status = PortalOperationStatus.Failed;
            o.FinishedAt = DateTimeOffset.UtcNow;
            o.Outcome = error.Length > 300 ? error[..300] + "…" : error;
        });

    private void Update(Guid opId, Action<PortalOperation> mutate)
    {
        lock (_gate)
        {
            var op = _ops.FirstOrDefault(o => o.Id == opId);
            if (op is null) return;
            mutate(op);
        }
        Notify();
    }

    private void Notify()
    {
        try { Changed?.Invoke(); }
        catch { /* a dead subscriber (disposed circuit) must not break the next state change */ }
    }
}
