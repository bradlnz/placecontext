using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PlaceContext.Data;
using PlaceContext.Infrastructure;
using PlaceContext.Jobs;
using PlaceContext.Vault;

namespace PlaceContext.Settings;

public static class SettingsInfrastructureDependencyInjection
{
    public static IServiceCollection AddSettingsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddInfrastructure(configuration);
        services.AddJobsInfrastructure(configuration);
        services.AddDataInfrastructure(configuration);
        services.AddVaultInfrastructure(configuration);
        // These legacy adapters currently register job/data workers along with their repositories.
        // Settings needs the repositories for import compatibility, but does not own those workers.
        services.RemoveAll<IHostedService>();
        return services;
    }
}
