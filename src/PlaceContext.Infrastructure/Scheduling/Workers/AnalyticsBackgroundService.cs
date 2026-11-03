using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Features;
using PlaceContext.Application.Operations;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>
/// Drains <see cref="AnalyticsRefreshQueue"/> in the background: for each queued project it sets
/// the tenant context and runs the chart sweep (<see cref="ProjectChartService.RefreshProjectAsync"/>).
/// One request at a time — local-LLM inference is CPU-bound, so parallel sweeps would only slow
/// each other down. Charts land in the store table by table; the Analytics tab picks them up as
/// they finish.
/// </summary>
public sealed class AnalyticsBackgroundService : BackgroundService
{
    private readonly AnalyticsRefreshQueue _queue;
    private readonly OperationCenter _ops;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AnalyticsBackgroundService> _log;

    public AnalyticsBackgroundService(AnalyticsRefreshQueue queue, OperationCenter ops, IServiceScopeFactory scopes,
        ILogger<AnalyticsBackgroundService> log)
    {
        _queue = queue;
        _ops = ops;
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var req in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            CurrentTenant.Set(req.Tenant);
            _ops.MarkRunning(req.OpId);
            try
            {
                _log.LogInformation("Analytics: refreshing {Scope} for project {ProjectId} ({Tenant})…",
                    req.TableName ?? "all tables", req.ProjectId, req.Tenant.Slug);
                await using var scope = _scopes.CreateAsyncScope();
                var charts = scope.ServiceProvider.GetRequiredService<ProjectChartService>();
                async Task RefreshAnalyticsAsync()
                {
                    if (req.TableName is null)
                    {
                        await charts.RefreshProjectAsync(req.ProjectId, stoppingToken);
                        return;
                    }
                    await charts.RefreshTableAsync(req.ProjectId, req.TableName, req.Instruction, stoppingToken);
                }
                await RefreshAnalyticsAsync();
                _ops.MarkDone(req.OpId, req.TableName is null ? "charts updated" : $"{req.TableName} redrawn");
            }
            catch (OperationCanceledException)
            {
                _ops.MarkFailed(req.OpId, "cancelled — host shutting down");
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Analytics: chart refresh failed for project {ProjectId}.", req.ProjectId);
                _ops.MarkFailed(req.OpId, ex.Message);
            }
            finally
            {
                CurrentTenant.Clear();
                _queue.MarkDone(req.ProjectId, req.TableName);
            }
        }
    }
}
