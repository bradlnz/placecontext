using PlaceContext.Application.Features;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>
/// Background worker that keeps the notifications pane in step with the persisted run statuses.
/// Every tick it sweeps each tenant's job/chain runs (via <see cref="RunStatusWatchService"/>) and
/// pushes transitions into the OperationCenter — so a run's terminal status reaches the bell the
/// moment it is committed, instead of when the in-process driver finally returns (minutes later
/// behind LLM enrichment), and runs executed by other replicas or via MCP/triggers appear too.
/// Runs on every replica by design: each replica feeds its own in-memory ledger.
/// </summary>
public sealed class RunStatusWatcherService : BackgroundService
{
    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RunStatusWatcherService> _log;
    private readonly Dictionary<Guid, RunWatchState> _states = new();

    public RunStatusWatcherService(IServiceScopeFactory scopes, ILogger<RunStatusWatcherService> log)
    {
        _scopes = scopes;
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
        var tenants = await LoadTenantsAsync(ct);
        foreach (var tenant in tenants)
        {
            if (!_states.TryGetValue(tenant.Id, out var state))
                _states[tenant.Id] = state = new RunWatchState();

            CurrentTenant.Set(tenant);
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<RunStatusWatchService>().SweepAsync(state, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.LogWarning(ex, "Run-status sweep failed for tenant {Slug}.", tenant.Slug); }
            finally { CurrentTenant.Clear(); }
        }

        foreach (var gone in _states.Keys.Where(id => tenants.All(t => t.Id != id)).ToList())
            _states.Remove(gone);
    }

    private async Task<IReadOnlyList<TenantInfo>> LoadTenantsAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tenants.AsNoTracking()
            .Select(t => new TenantInfo(t.Id, t.Slug, t.Name, t.TimeZoneId)).ToListAsync(ct);
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
