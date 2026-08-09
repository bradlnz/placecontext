using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Infrastructure;

namespace PlaceContext.Projects;

public static class ProjectsInfrastructureDependencyInjection
{
    public static IServiceCollection AddProjectsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
        => services.AddInfrastructure(configuration);
}
