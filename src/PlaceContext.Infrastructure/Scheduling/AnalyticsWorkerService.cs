using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Features;
using PlaceContext.Infrastructure.Operations;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>
/// Drains <see cref="AnalyticsRefreshQueue"/> in the background: for each queued project it sets
/// the tenant context and runs the chart sweep (<see cref="ProjectChartService.RefreshProjectAsync"/>).
/// One request at a time — local-LLM inference is CPU-bound, so parallel sweeps would only slow
/// each other down. Charts land in the store table by table; the Analytics tab picks them up as
/// they finish.
/// </summary>
public sealed class AnalyticsWorkerService : BackgroundService
{
    private readonly AnalyticsRefreshQueue _queue;
    private readonly OperationCenter _ops;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AnalyticsWorkerService> _log;

    public AnalyticsWorkerService(AnalyticsRefreshQueue queue, OperationCenter ops, IServiceScopeFactory scopes,
        ILogger<AnalyticsWorkerService> log)
    {
        _queue = queue;
        _ops = ops;
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (tenant, projectId, opId) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            CurrentTenant.Set(tenant);
            _ops.MarkRunning(opId);
            try
            {
                _log.LogInformation("Analytics: refreshing charts for project {ProjectId} ({Tenant})…",
                    projectId, tenant.Slug);
                await using var scope = _scopes.CreateAsyncScope();
                var charts = scope.ServiceProvider.GetRequiredService<ProjectChartService>();
                await charts.RefreshProjectAsync(projectId, stoppingToken);
                _log.LogInformation("Analytics: charts refreshed for project {ProjectId}.", projectId);
                _ops.MarkDone(opId, "charts updated");
            }
            catch (OperationCanceledException) { _ops.MarkFailed(opId, "cancelled — host shutting down"); throw; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Analytics: chart refresh failed for project {ProjectId}.", projectId);
                _ops.MarkFailed(opId, ex.Message);
            }
            finally
            {
                CurrentTenant.Clear();
                _queue.MarkDone(projectId);
            }
        }
    }
}
