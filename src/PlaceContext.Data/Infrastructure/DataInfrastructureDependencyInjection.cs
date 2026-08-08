using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Infrastructure.Persistence;
using PlaceContext.Data.Infrastructure.ProjectData;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Data;

public static class DataInfrastructureDependencyInjection
{
    public static IServiceCollection AddDataInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IDataMappingRepository, EfDataMappingRepository>();
        services.AddScoped<IDataEntityRepository, EfDataEntityRepository>();
        services.AddScoped<IEntityTagStore, EfEntityTagStore>();
        services.AddScoped<IRecordLinkStore, EfRecordLinkStore>();
        services.AddScoped<IProjectChartRepository, EfProjectChartRepository>();
        services.AddScoped<IProjectDatabaseConnectionResolver, ProjectDatabaseConnectionResolver>();
        services.AddScoped<IProjectDataStore, NpgsqlProjectDataStore>();
        return services;
    }
}
