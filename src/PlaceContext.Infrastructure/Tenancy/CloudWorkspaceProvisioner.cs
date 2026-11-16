using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Billing;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Tenancy;

public sealed class CloudWorkspaceProvisioner(IServiceScopeFactory scopes,
    ClusterTenantRuntime runtime, IOptions<RunBillingDeploymentOptions> options,
    ILogger<CloudWorkspaceProvisioner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ManagedHosting) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (await ProcessOneAsync(db, runtime.PrepareAsync, logger, stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Cloud workspace provisioning worker failed."); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    internal static async Task<bool> ProcessOneAsync(AppDbContext db,
        Func<Guid, string, CancellationToken, Task<string>> prepare,
        ILogger logger, CancellationToken ct)
    {
        CloudWorkspaceRow work;
        var token = Guid.NewGuid();
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE cloud_workspaces SET "State" = 'failed', "LeaseToken" = NULL
                WHERE "State" = 'provisioning' AND "Attempts" >= 5 AND "NextAttemptAt" <= now()
                """, ct);
            // Cross-tenant worker claim; the lease is fenced so an expired worker cannot finish a newer attempt.
            var candidates = await db.CloudWorkspaces.FromSqlRaw("""
                SELECT * FROM cloud_workspaces
                WHERE "State" <> 'ready' AND "Attempts" < 5 AND "NextAttemptAt" <= now()
                ORDER BY "NextAttemptAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
            if (candidates.Count == 0)
            {
                await tx.CommitAsync(ct);
                return false;
            }
            work = candidates[0];
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cloud_workspaces SET "State" = 'provisioning', "Attempts" = "Attempts" + 1,
                    "LeaseToken" = {token}, "NextAttemptAt" = now() + interval '3 minutes'
                WHERE "TenantId" = {work.TenantId}
                """, ct);
            await tx.CommitAsync(ct);
        }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            // Paid plan changes can precede the first provisioning attempt.
            var plan = await db.TenantRunPlans.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == work.TenantId && x.Plan != RunPlans.Developer)
                .Select(x => x.Plan).SingleOrDefaultAsync(timeout.Token) ?? work.Plan;
            await prepare(work.TenantId, plan, timeout.Token);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cloud_workspaces SET "State" = 'ready', "LeaseToken" = NULL
                WHERE "TenantId" = {work.TenantId} AND "LeaseToken" = {token}
                """, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cloud workspace provisioning failed for tenant {TenantId}, attempt {Attempt}.",
                work.TenantId, work.Attempts + 1);
            var retryAt = DateTimeOffset.UtcNow.AddSeconds(10 * Math.Pow(2, work.Attempts));
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cloud_workspaces SET "State" = 'failed', "LeaseToken" = NULL,
                    "NextAttemptAt" = {retryAt}
                WHERE "TenantId" = {work.TenantId} AND "LeaseToken" = {token}
                """, ct);
        }
        return true;
    }
}
