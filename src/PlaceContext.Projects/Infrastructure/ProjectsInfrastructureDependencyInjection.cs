using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Infrastructure;
using PlaceContext.Projects.Infrastructure.Integration;
using PlaceContext.Projects.Integration;

namespace PlaceContext.Projects;

public static class ProjectsInfrastructureDependencyInjection
{
    public static IServiceCollection AddProjectsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddInfrastructure(configuration);
        services.AddHttpClient();
        services.AddScoped<IProjectGraphClient, HttpProjectGraphClient>();
        services.AddScoped<IProjectEventPublisher, HttpProjectEventPublisher>();
        return services;
    }
}
