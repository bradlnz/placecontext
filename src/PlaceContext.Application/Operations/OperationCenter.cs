using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Tenancy;

namespace PlaceContext.Application.Operations;

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
    private readonly CancellationToken _stoppingToken;
    private readonly ILogger<OperationCenter>? _log;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, PortalOperation> _ops = new();
    private readonly Dictionary<Guid, List<PortalOperation>> _tenantOps = new();
    private readonly Dictionary<Guid, List<Subscription>> _subscriptions = new();

    public OperationCenter(IServiceScopeFactory scopes, CancellationToken stoppingToken,
        ILogger<OperationCenter>? log = null)
    {
        _scopes = scopes;
        _stoppingToken = stoppingToken;
        _log = log;
    }

    public IDisposable Subscribe(Guid tenantId, Action changed, Guid? userId = null, Guid? projectId = null, string? correlationKey = null)
    {
        var subscription = new Subscription(this, tenantId, changed, userId, projectId, correlationKey);
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(tenantId, out var subscribers))
            {
                _subscriptions[tenantId] = subscribers = [];
            }
            subscribers.Add(subscription);
        }
        return subscription;
    }

    public IReadOnlyList<PortalOperation> ListForTenant(Guid tenantId, Guid? userId = null)
    {
        lock (_gate)
        {
            return _tenantOps.TryGetValue(tenantId, out var operations)
                        ? operations.Where(o => o.UserId is null || o.UserId == userId)
                            .OrderByDescending(o => o.Status == PortalOperationStatus.NeedsInput)
                            .ThenByDescending(o => o.QueuedAt).ToList()
                        : [];
        }
    }

    public int NeedsInputCount(Guid tenantId, Guid? userId = null)
    {
        lock (_gate)
        {
            return _tenantOps.TryGetValue(tenantId, out var operations)
                        ? operations.Count(o => (o.UserId is null || o.UserId == userId)
                            && o.Status == PortalOperationStatus.NeedsInput)
                        : 0;
        }
    }

    public int ActiveCount(Guid tenantId, Guid? userId = null)
    {
        lock (_gate)
        {
            return _tenantOps.TryGetValue(tenantId, out var operations)
                        ? operations.Count(o => (o.UserId is null || o.UserId == userId)
                            && o.Status is PortalOperationStatus.Queued or PortalOperationStatus.Running)
                        : 0;
        }
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
                var outcome = await work(scope.ServiceProvider, _stoppingToken);
                MarkDone(op.Id, outcome);
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Portal operation failed: {Title}", title);
                MarkFailed(op.Id, ex.Message);
            }
            finally
            {
                CurrentTenant.Clear();
            }
        });
        return op;
    }

    /// <summary>Register an operation whose lifecycle an external worker drives (e.g. the analytics sweep).</summary>
    public PortalOperation Track(TenantInfo tenant, Guid? projectId, string title, string? link,
        string? correlationKey = null, Guid? userId = null)
    {
        var op = new PortalOperation
        {
            TenantId = tenant.Id,
            ProjectId = projectId,
            Title = title,
            Link = link,
            CorrelationKey = correlationKey,
            UserId = userId,
        };
        lock (_gate)
        {
            AddAndTrim(op);
        }
        Notify(op);
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
        PortalOperation op;
        lock (_gate)
        {
            op = _tenantOps.GetValueOrDefault(tenantId)?.FirstOrDefault(o => o.CorrelationKey == correlationKey)!;
            if (op?.Sealed == true)
            {
                return;
            }
            PortalOperation UpdateOperation()
            {
                if (op is null)
                {
                    op = new PortalOperation
                    {
                        TenantId = tenantId,
                        ProjectId = projectId,
                        Title = title,
                        Link = link,
                        CorrelationKey = correlationKey,
                    };
                    op.Status = status;
                    op.StartedAt = startedAt;
                    op.FinishedAt = finishedAt;
                    op.Outcome = outcome;
                    op.Sealed = terminal;
                    AddAndTrim(op);
                    return op;
                }
                op.Status = status;
                if (link is not null)
                {
                    op.Link = link;
                }
                op.StartedAt ??= startedAt;
                op.FinishedAt = finishedAt ?? op.FinishedAt;
                if (outcome is not null)
                {
                    op.Outcome = outcome;
                }
                op.Sealed = terminal;

                return op;
            }
            op = UpdateOperation();
        }
        Notify(op);
    }

    public void ReportProgress(Guid opId)
    {
        PortalOperation? operation;
        lock (_gate)
        {
            operation = _ops.GetValueOrDefault(opId);
        }
        if (operation is not null)
        {
            Notify(operation);
        }
    }

    public void MarkRunning(Guid opId) => Update(opId, o =>
    {
        if (o.Sealed || o.Status is PortalOperationStatus.NeedsInput or PortalOperationStatus.Succeeded or PortalOperationStatus.Failed)
        {
            return false;
        }
        o.Status = PortalOperationStatus.Running;
        o.StartedAt = DateTimeOffset.UtcNow;
        return true;
    });

    public void MarkDone(Guid opId, string? outcome = null)
        => Update(opId, o =>
        {
            if (o.Sealed || o.Status == PortalOperationStatus.NeedsInput)
            {
                return false;
            }
            o.Status = PortalOperationStatus.Succeeded;
            o.FinishedAt = DateTimeOffset.UtcNow;
            o.Outcome = outcome;
            return true;
        });

    public void MarkFailed(Guid opId, string error)
        => Update(opId, o =>
        {
            if (o.Sealed || o.Status == PortalOperationStatus.NeedsInput)
            {
                return false;
            }
            o.Status = PortalOperationStatus.Failed;
            o.FinishedAt = DateTimeOffset.UtcNow;
            o.Outcome = error.Length > 300 ? error[..300] + "…" : error;
            return true;
        });

    /// <summary>Callers hold <see cref="_gate"/>.</summary>
    private void AddAndTrim(PortalOperation op)
    {
        _ops.Add(op.Id, op);
        if (!_tenantOps.TryGetValue(op.TenantId, out var tenantOps))
        {
            _tenantOps[op.TenantId] = tenantOps = [];
        }
        tenantOps.Add(op);
        foreach (var stale in tenantOps.OrderByDescending(o => o.QueuedAt).Skip(KeepPerTenant)
                     .Where(o => o.Status is PortalOperationStatus.Succeeded or PortalOperationStatus.Failed).ToArray())
        {
            tenantOps.Remove(stale);
            _ops.Remove(stale.Id);
        }
    }

    private void Update(Guid opId, Func<PortalOperation, bool> mutate)
    {
        PortalOperation? op;
        lock (_gate)
        {
            op = _ops.GetValueOrDefault(opId);
            if (op is null || !mutate(op))
            {
                return;
            }
        }
        Notify(op);
    }

    private void Notify(PortalOperation operation)
    {
        Subscription[] subscribers;
        lock (_gate)
        {
            subscribers = _subscriptions.TryGetValue(operation.TenantId, out var list)
                        ? list.Where(s => (operation.UserId is null || operation.UserId == s.UserId)
                            && (s.ProjectId is null || operation.ProjectId is null || operation.ProjectId == s.ProjectId)
                            && (s.CorrelationKey is null || operation.CorrelationKey == s.CorrelationKey)).ToArray()
                        : [];
        }
        foreach (var subscriber in subscribers)
        {
            try
            {
                subscriber.Notify();
            }
            catch { /* A disconnected circuit must not prevent other subscribers from updating. */ }
        }
    }

    private sealed class Subscription(OperationCenter owner, Guid tenantId, Action changed,
        Guid? userId, Guid? projectId, string? correlationKey) : IDisposable
    {
        // Callbacks must keep the subscribing circuit's identity, not the background publisher's.
        private readonly ExecutionContext? _context = ExecutionContext.Capture();
        private int _disposed;
        public Guid? UserId { get; } = userId;
        public Guid? ProjectId { get; } = projectId;
        public string? CorrelationKey { get; } = correlationKey;

        public void Notify()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }
            if (_context is null)
            {
                changed();
                return;
            }
            ExecutionContext.Run(_context, _ => changed(), null);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }
            lock (owner._gate)
            {
                if (!owner._subscriptions.TryGetValue(tenantId, out var list))
                {
                    return;
                }
                list.Remove(this);
                if (list.Count == 0)
                {
                    owner._subscriptions.Remove(tenantId);
                }
            }
        }
    }
}
