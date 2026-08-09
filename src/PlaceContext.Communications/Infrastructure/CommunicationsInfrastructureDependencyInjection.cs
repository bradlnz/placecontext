using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Communications;

public static class CommunicationsInfrastructureDependencyInjection
{
    public static IServiceCollection AddCommunicationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = configuration;
        services.AddHealthChecks();
        return services;
    }
}
