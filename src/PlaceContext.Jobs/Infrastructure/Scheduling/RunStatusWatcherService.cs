using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PlaceContext.Jobs.Infrastructure.Scheduling;

/// <summary>
/// Background worker that keeps the notifications pane in step with the persisted run statuses.
/// Every tick it sweeps each tenant's job/chain runs (via <see cref="RunStatusWatchService"/>) and
/// pushes transitions through the notification port — so a run's terminal status reaches the bell the
/// moment it is committed, instead of when the in-process driver finally returns (minutes later
/// behind LLM enrichment), and runs executed by other replicas or via MCP/triggers appear too.
/// Runs on every replica by design: each replica feeds its own in-memory ledger.
/// </summary>
public sealed class RunStatusWatcherService : BackgroundService
{
    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TenantCacheInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly ICurrentTenantAccessor _tenantAccessor;
    private readonly ILogger<RunStatusWatcherService> _log;
    private readonly Dictionary<Guid, RunWatchState> _states = new();
    private IReadOnlyList<TenantContext> _cachedTenants = [];
    private DateTimeOffset _tenantsRefreshAt = DateTimeOffset.MinValue;

    public RunStatusWatcherService(
        IServiceScopeFactory scopes,
        ICurrentTenantAccessor tenantAccessor,
        ILogger<RunStatusWatcherService> log)
    {
        _scopes = scopes;
        _tenantAccessor = tenantAccessor;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(WatchInterval);
        do
        {
            try { await SweepAllTenantsAsync(stoppingToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.LogError(ex, "Run-status sweep failed; will retry next tick."); }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private async Task SweepAllTenantsAsync(CancellationToken ct)
    {
        var tenants = await GetTenantsAsync(ct);
        foreach (var tenant in tenants)
        {
            if (!_states.TryGetValue(tenant.Id, out var state))
                _states[tenant.Id] = state = new RunWatchState();

            _tenantAccessor.Set(tenant);
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<RunStatusWatchService>().SweepAsync(state, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.LogWarning(ex, "Run-status sweep failed for tenant {Slug}.", tenant.Slug); }
            finally { _tenantAccessor.Clear(); }
        }

        foreach (var gone in _states.Keys.Where(id => tenants.All(t => t.Id != id)).ToList())
            _states.Remove(gone);
    }

    private async Task<IReadOnlyList<TenantContext>> GetTenantsAsync(CancellationToken ct)
    {
        if (_cachedTenants.Count > 0 && DateTimeOffset.UtcNow < _tenantsRefreshAt)
            return _cachedTenants;

        await using var scope = _scopes.CreateAsyncScope();
        _cachedTenants = await scope.ServiceProvider.GetRequiredService<ITenantCatalog>().ListAsync(ct);
        _tenantsRefreshAt = DateTimeOffset.UtcNow + TenantCacheInterval;
        return _cachedTenants;
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
