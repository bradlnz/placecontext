using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Services;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Crm.Infrastructure.Scheduling;

/// <summary>
/// One startup pass repairs artifacts produced before terminal chain association was introduced and
/// retries recent runs whose immediate best-effort association encountered a transient failure.
/// </summary>
public sealed class CrmArtifactReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ICurrentTenantAccessor _tenantAccessor;
    private readonly ILogger<CrmArtifactReconciliationWorker> _log;

    public CrmArtifactReconciliationWorker(
        IServiceScopeFactory scopes,
        ICurrentTenantAccessor tenantAccessor,
        ILogger<CrmArtifactReconciliationWorker> log)
        => (_scopes, _tenantAccessor, _log) = (scopes, tenantAccessor, log);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var rootScope = _scopes.CreateAsyncScope();
            var tenants = await rootScope.ServiceProvider.GetRequiredService<ITenantCatalog>()
                .ListAsync(stoppingToken);

            var associated = 0;
            foreach (var tenant in tenants)
            {
                _tenantAccessor.Set(tenant);
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
                finally { _tenantAccessor.Clear(); }
            }

            if (associated > 0)
                _log.LogInformation("Associated {Count} existing run artifacts with CRM customers.", associated);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _log.LogError(ex, "CRM artifact reconciliation failed.");
        }
        finally { _tenantAccessor.Clear(); }
    }
}
