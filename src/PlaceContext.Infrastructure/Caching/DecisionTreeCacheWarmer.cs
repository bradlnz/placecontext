using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Caching;

/// <summary>
/// Pre-builds every project's decision tree on startup and seeds the in-memory cache so the first
/// portal open for each project is served instantly. Runs as a background service so it doesn't
/// block the host from accepting requests; failures for one tenant or project are logged and do
/// not stop warming for the rest.
/// </summary>
public sealed class DecisionTreeCacheWarmer : BackgroundService
{
    private readonly IServiceProvider _rootProvider;
    private readonly ILogger<DecisionTreeCacheWarmer> _logger;
    private readonly bool _enabled;

    public DecisionTreeCacheWarmer(IServiceProvider rootProvider, IConfiguration configuration, ILogger<DecisionTreeCacheWarmer> logger)
    {
        _rootProvider = rootProvider;
        _logger = logger;
        _enabled = configuration.GetValue("PlaceContext:Graph:WarmOnStartup", true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Decision-tree cache warmer is disabled (PlaceContext:Graph:WarmOnStartup=false).");
            return;
        }

        try
        {
            await WarmAllTenantsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Decision-tree cache warmer cancelled during startup.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Decision-tree cache warmer failed.");
        }
    }

    private async Task WarmAllTenantsAsync(CancellationToken ct)
    {
        IReadOnlyList<TenantInfo> tenants;
        using (var listScope = _rootProvider.CreateScope())
        {
            var store = listScope.ServiceProvider.GetRequiredService<ITenantStore>();
            tenants = await store.ListTenantsAsync(take: 10_000, ct);
        }

        _logger.LogInformation("Warming decision-tree cache for {TenantCount} tenant(s).", tenants.Count);

        foreach (var tenant in tenants)
        {
            if (ct.IsCancellationRequested) return;
            await WarmTenantAsync(tenant, ct);
        }

        _logger.LogInformation("Decision-tree cache warmer finished.");
    }

    private async Task WarmTenantAsync(TenantInfo tenant, CancellationToken ct)
    {
        using var scope = _rootProvider.CreateScope();
        CurrentTenant.Set(tenant);

        try
        {
            var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
            var projectList = await projects.ListAsync(ct);

            if (projectList.Count == 0)
            {
                _logger.LogDebug("No projects to warm for tenant {TenantSlug}.", tenant.Slug);
                return;
            }

            var provider = scope.ServiceProvider.GetRequiredService<IDecisionTreeProvider>();
            var warmed = 0;

            foreach (var project in projectList)
            {
                if (ct.IsCancellationRequested) return;

                try
                {
                    await provider.BuildAsync(ProjectId.From(project.Id.Value), ct);
                    warmed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to warm decision tree for project {ProjectId} in tenant {TenantSlug}.",
                        project.Id.Value, tenant.Slug);
                }
            }

            _logger.LogInformation(
                "Warmed {WarmedCount}/{ProjectCount} decision tree(s) for tenant {TenantSlug}.",
                warmed, projectList.Count, tenant.Slug);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to warm tenant {TenantSlug}.", tenant.Slug);
        }
    }
}
