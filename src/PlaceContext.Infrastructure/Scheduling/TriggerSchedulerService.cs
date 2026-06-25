using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>
/// Background worker that drives triggers. Two concurrent loops:
/// <list type="number">
/// <item>a periodic scan that fires due cron-schedule triggers across every tenant; and</item>
/// <item>a queue drain that executes the job runs enqueued by schedule firing and event fan-out.</item>
/// </list>
/// Each unit of work sets the ambient tenant (AsyncLocal) before opening a DI scope so the tenant
/// query filter applies correctly — triggers fire outside any HTTP request.
///
/// Single-process today. On k3s this must run as a singleton or use leader-election so schedules fire
/// once across replicas (see the k3s deployment task).
/// </summary>
public sealed class TriggerSchedulerService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(20);

    private readonly IServiceScopeFactory _scopes;
    private readonly InMemoryJobRunQueue _queue;
    private readonly ILogger<TriggerSchedulerService> _log;

    public TriggerSchedulerService(
        IServiceScopeFactory scopes, IJobRunQueue queue, ILogger<TriggerSchedulerService> log)
    {
        _scopes = scopes;
        // The hosted service needs the channel reader; the concrete queue is registered as the singleton.
        _queue = (InMemoryJobRunQueue)queue;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var scanLoop = ScanSchedulesLoopAsync(stoppingToken);
        var drainLoop = DrainRunQueueLoopAsync(stoppingToken);
        await Task.WhenAll(scanLoop, drainLoop);
    }

    // ── Loop 1: fire due schedules across all tenants ──────────────────────────────────────────────

    private async Task ScanSchedulesLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(ScanInterval);
        do
        {
            try
            {
                foreach (var tenant in await LoadTenantsAsync(ct))
                {
                    CurrentTenant.Set(tenant);
                    try
                    {
                        await using var scope = _scopes.CreateAsyncScope();
                        var scanner = scope.ServiceProvider.GetRequiredService<ScheduleScanService>();
                        var fired = await scanner.FireDueAsync(ct);
                        if (fired > 0)
                            _log.LogInformation("Fired {Count} schedule trigger(s) for tenant {Slug}.", fired, tenant.Slug);
                    }
                    finally
                    {
                        CurrentTenant.Clear();
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Schedule scan failed; will retry next interval.");
            }
        }
        while (await SafeWaitAsync(timer, ct));
    }

    // ── Loop 2: execute enqueued runs ──────────────────────────────────────────────────────────────

    private async Task DrainRunQueueLoopAsync(CancellationToken ct)
    {
        await foreach (var run in _queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                var tenant = await FindTenantAsync(run.TenantId, ct);
                if (tenant is null)
                {
                    _log.LogWarning("Dropping queued run for unknown tenant {TenantId}.", run.TenantId);
                    continue;
                }

                CurrentTenant.Set(tenant);
                try
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
                    await dispatcher.Send(new RunJobCommand(run.JobId, run.Payload), ct);
                    _log.LogInformation("Trigger '{Trigger}' ran job {JobId} for tenant {Slug}.",
                        run.TriggerName, run.JobId, tenant.Slug);
                }
                finally
                {
                    CurrentTenant.Clear();
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Queued job run {JobId} failed.", run.JobId);
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<TenantInfo>> LoadTenantsAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tenants.AsNoTracking()
            .Select(t => new TenantInfo(t.Id, t.Slug, t.Name, t.TimeZoneId))
            .ToListAsync(ct);
    }

    private async Task<TenantInfo?> FindTenantAsync(Guid tenantId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new TenantInfo(t.Id, t.Slug, t.Name, t.TimeZoneId))
            .FirstOrDefaultAsync(ct);
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
