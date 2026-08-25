using PlaceContext.Application.Features;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
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
    public static readonly TimeSpan DefaultWatchInterval = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan MinimumWatchInterval = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan MaximumWatchInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TenantCacheInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RunStatusWatcherService> _log;
    private readonly TimeSpan _watchInterval;
    private readonly Dictionary<Guid, RunWatchState> _states = new();
    private List<TenantInfo> _cachedTenants = new();
    private DateTimeOffset _tenantsRefreshAt = DateTimeOffset.MinValue;

    public RunStatusWatcherService(
        IServiceScopeFactory scopes,
        ILogger<RunStatusWatcherService> log,
        IConfiguration configuration)
    {
        _scopes = scopes;
        _log = log;
        _watchInterval = ResolveWatchInterval(configuration);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "Run-status notifications polling every {IntervalMilliseconds} ms.",
            _watchInterval.TotalMilliseconds
        );
        using var timer = new PeriodicTimer(_watchInterval);
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

    private async Task<IReadOnlyList<TenantInfo>> GetTenantsAsync(CancellationToken ct)
    {
        if (_cachedTenants.Count > 0 && DateTimeOffset.UtcNow < _tenantsRefreshAt)
            return _cachedTenants;

        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _cachedTenants = await db.Tenants.AsNoTracking()
            .Select(t => new TenantInfo(t.Id, t.Slug, t.Name, t.TimeZoneId)).ToListAsync(ct);
        _tenantsRefreshAt = DateTimeOffset.UtcNow + TenantCacheInterval;
        return _cachedTenants;
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    internal static TimeSpan ResolveWatchInterval(IConfiguration configuration)
    {
        var seconds = configuration.GetValue<double?>(
            "PlaceContext:RunStatusWatcher:IntervalSeconds"
        ) ?? DefaultWatchInterval.TotalSeconds;
        if (!double.IsFinite(seconds))
            return DefaultWatchInterval;

        return TimeSpan.FromSeconds(
            Math.Clamp(
                seconds,
                MinimumWatchInterval.TotalSeconds,
                MaximumWatchInterval.TotalSeconds
            )
        );
    }
}
