using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>Repair failed run tracking independently of notification polling and available execution slots.</summary>
public sealed class FailedRunReconciliationBackgroundService(
    IServiceScopeFactory scopes, ILogger<FailedRunReconciliationBackgroundService> log) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("Failed-run reconciliation runs at startup and every {Seconds} seconds.", Interval.TotalSeconds);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed-run reconciliation sweep failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task SweepAsync(CancellationToken ct)
    {
        await using var discovery = scopes.CreateAsyncScope();
        var db = discovery.ServiceProvider.GetRequiredService<AppDbContext>();
        // This existing idempotent operation reconciles standalone receipts across tenants.
        var receipts = await RpcJobSubmissionBackgroundService.ReconcileFinishedAsync(db, ct);
        var tenants = await db.Tenants.AsNoTracking()
            .Select(t => new TenantInfo(t.Id, t.Slug, t.Name, t.TimeZoneId)).ToListAsync(ct);
        var workflows = 0;
        foreach (var tenant in tenants)
        {
            ct.ThrowIfCancellationRequested();
            CurrentTenant.Set(tenant);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                workflows += await scope.ServiceProvider.GetRequiredService<DbRunStatusReconciler>().ReconcileAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Failed-run reconciliation failed for tenant {TenantId}; continuing.", tenant.Id);
            }
            finally
            {
                CurrentTenant.Clear();
            }
        }
        var queues = await RpcChainSubmissionBackgroundService.ReconcileFailuresAsync(db, ct);
        log.LogInformation("Failed-run reconciliation completed: {Receipts} job receipts, {Workflows} workflows repaired; {Queues} queue failures retried or closed by policy.", receipts, workflows, queues);
    }
}
