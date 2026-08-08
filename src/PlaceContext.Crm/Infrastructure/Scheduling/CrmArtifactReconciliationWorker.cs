using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Crm.Infrastructure.Scheduling;

/// <summary>
/// One startup pass repairs artifacts produced before terminal chain association was introduced and
/// retries recent runs whose immediate best-effort association encountered a transient failure.
/// </summary>
public sealed class CrmArtifactReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CrmArtifactReconciliationWorker> _log;

    public CrmArtifactReconciliationWorker(
        IServiceScopeFactory scopes,
        ILogger<CrmArtifactReconciliationWorker> log)
        => (_scopes, _log) = (scopes, log);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var rootScope = _scopes.CreateAsyncScope();
            var db = rootScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenants = await db.Tenants.AsNoTracking()
                .Select(row => new TenantInfo(row.Id, row.Slug, row.Name, row.TimeZoneId))
                .ToListAsync(stoppingToken);

            var associated = 0;
            foreach (var tenant in tenants)
            {
                CurrentTenant.Set(tenant);
                try
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    var runs = scope.ServiceProvider.GetRequiredService<IChainRunRepository>();
                    var linker = scope.ServiceProvider.GetRequiredService<CrmArtifactAssociationService>();
                    foreach (var run in (await runs.ListRecentAsync(200, stoppingToken))
                                 .Where(run => run.CrmClientId is not null
                                     && run.Status is not (ChainRunStatus.Running or ChainRunStatus.Waiting)))
                        associated += await linker.AssociateAsync(run, stoppingToken);
                }
                finally { CurrentTenant.Clear(); }
            }

            if (associated > 0)
                _log.LogInformation("Associated {Count} existing run artifacts with CRM customers.", associated);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _log.LogError(ex, "CRM artifact reconciliation failed.");
        }
        finally { CurrentTenant.Clear(); }
    }
}
