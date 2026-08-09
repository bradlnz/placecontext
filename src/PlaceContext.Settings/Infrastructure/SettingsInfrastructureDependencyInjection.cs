using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Settings;

public static class SettingsInfrastructureDependencyInjection
{
    public static IServiceCollection AddSettingsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = configuration;
        services.AddHealthChecks();
        return services;
    }
}
