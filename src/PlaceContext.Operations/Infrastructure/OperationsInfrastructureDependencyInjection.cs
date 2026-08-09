using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Operations;

public static class OperationsInfrastructureDependencyInjection
{
    public static IServiceCollection AddOperationsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        services.AddHealthChecks();
        return services;
    }
}
