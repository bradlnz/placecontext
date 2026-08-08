using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Operations;

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
        Func<IServiceProvider, CancellationToken, Task<string?>> work, string? correlationKey = null)
    {
        var op = Track(tenant, projectId, title, link, correlationKey);
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
    public PortalOperation Track(TenantInfo tenant, Guid? projectId, string title, string? link,
        string? correlationKey = null)
    {
        var op = new PortalOperation
        {
            TenantId = tenant.Id, ProjectId = projectId, Title = title, Link = link,
            CorrelationKey = correlationKey,
        };
        lock (_gate) { AddAndTrim(op); }
        Notify();
        return op;
    }

    /// <summary>
    /// Upsert by (tenant, correlation key) from an authoritative source — the run-status watcher
    /// reflecting persisted run state. Creates the operation when no page tracked it here (a run
    /// queued by a trigger, started via MCP, or executed on another replica); otherwise converges
    /// on the existing entry, keeping its (often richer) title. A terminal Sync seals the
    /// operation: later advisory Mark* calls — which arrive minutes late behind best-effort
    /// enrichment, or with the wrong status — can no longer change it.
    /// </summary>
    public void Sync(Guid tenantId, string correlationKey, PortalOperationStatus status, string title,
        string? link, Guid? projectId, string? outcome, DateTimeOffset? startedAt, DateTimeOffset? finishedAt)
    {
        var terminal = status is PortalOperationStatus.Succeeded or PortalOperationStatus.Failed;
        lock (_gate)
        {
            var op = _ops.FirstOrDefault(o => o.TenantId == tenantId && o.CorrelationKey == correlationKey);
            if (op is null)
            {
                op = new PortalOperation
                {
                    TenantId = tenantId, ProjectId = projectId, Title = title, Link = link,
                    CorrelationKey = correlationKey,
                };
                op.Status = status;
                op.StartedAt = startedAt;
                op.FinishedAt = finishedAt;
                op.Outcome = outcome;
                op.Sealed = terminal;
                AddAndTrim(op);
            }
            else
            {
                if (op.Sealed) return;
                op.Status = status;
                op.StartedAt ??= startedAt;
                op.FinishedAt = finishedAt ?? op.FinishedAt;
                if (outcome is not null) op.Outcome = outcome;
                op.Sealed = terminal;
            }
        }
        Notify();
    }

    public void MarkRunning(Guid opId) => Update(opId, o =>
    {
        if (o.Sealed || o.Status is PortalOperationStatus.Succeeded or PortalOperationStatus.Failed) return false;
        o.Status = PortalOperationStatus.Running;
        o.StartedAt = DateTimeOffset.UtcNow;
        return true;
    });

    public void MarkDone(Guid opId, string? outcome = null)
        => Update(opId, o =>
        {
            if (o.Sealed) return false;
            o.Status = PortalOperationStatus.Succeeded;
            o.FinishedAt = DateTimeOffset.UtcNow;
            o.Outcome = outcome;
            return true;
        });

    public void MarkFailed(Guid opId, string error)
        => Update(opId, o =>
        {
            if (o.Sealed) return false;
            o.Status = PortalOperationStatus.Failed;
            o.FinishedAt = DateTimeOffset.UtcNow;
            o.Outcome = error.Length > 300 ? error[..300] + "…" : error;
            return true;
        });

    /// <summary>Callers hold <see cref="_gate"/>.</summary>
    private void AddAndTrim(PortalOperation op)
    {
        _ops.Add(op);
        var tenantOps = _ops.Where(o => o.TenantId == op.TenantId).OrderByDescending(o => o.QueuedAt).ToList();
        foreach (var stale in tenantOps.Skip(KeepPerTenant)
                     .Where(o => o.Status is PortalOperationStatus.Succeeded or PortalOperationStatus.Failed))
            _ops.Remove(stale);
    }

    private void Update(Guid opId, Func<PortalOperation, bool> mutate)
    {
        lock (_gate)
        {
            var op = _ops.FirstOrDefault(o => o.Id == opId);
            if (op is null || !mutate(op)) return;
        }
        Notify();
    }

    private void Notify()
    {
        try { Changed?.Invoke(); }
        catch { /* a dead subscriber (disposed circuit) must not break the next state change */ }
    }
}
