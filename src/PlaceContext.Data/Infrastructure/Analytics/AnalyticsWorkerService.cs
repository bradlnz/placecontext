using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Analytics;

namespace PlaceContext.Data.Infrastructure.Analytics;

/// <summary>Serially drains Data-owned analytics refresh requests.</summary>
public sealed class AnalyticsWorkerService(
    AnalyticsRefreshQueue queue,
    IBackgroundOperationNotifier operations,
    ICurrentTenantAccessor tenantAccessor,
    IServiceScopeFactory scopes,
    ILogger<AnalyticsWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            tenantAccessor.Set(request.Tenant);
            operations.MarkRunning(request.OperationId);
            try
            {
                logger.LogInformation(
                    "Analytics: refreshing {Scope} for project {ProjectId} ({Tenant}).",
                    request.TableName ?? "all tables",
                    request.ProjectId,
                    request.Tenant.Slug);

                await using var scope = scopes.CreateAsyncScope();
                var charts = scope.ServiceProvider.GetRequiredService<IProjectChartRefresher>();
                if (request.TableName is null)
                    await charts.RefreshProjectAsync(request.ProjectId, stoppingToken);
                else
                    await charts.RefreshTableAsync(
                        request.ProjectId,
                        request.TableName,
                        request.Instruction,
                        stoppingToken);

                operations.MarkDone(
                    request.OperationId,
                    request.TableName is null ? "charts updated" : $"{request.TableName} redrawn");
            }
            catch (OperationCanceledException)
            {
                operations.MarkFailed(request.OperationId, "cancelled — service shutting down");
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Analytics: chart refresh failed for project {ProjectId}.",
                    request.ProjectId);
                operations.MarkFailed(request.OperationId, exception.Message);
            }
            finally
            {
                tenantAccessor.Clear();
                queue.MarkDone(request.ProjectId, request.TableName);
            }
        }
    }
}
