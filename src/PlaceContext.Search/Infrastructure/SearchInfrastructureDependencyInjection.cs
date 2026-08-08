using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Ports;
using PlaceContext.Search.Infrastructure.OpenSearch;
using PlaceContext.Search.Infrastructure.Persistence;

namespace PlaceContext.Search;

public static class SearchInfrastructureDependencyInjection
{
    public static IServiceCollection AddSearchInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OpenSearchOptions>(
            configuration.GetSection("PlaceContext:OpenSearch"));
        services.AddScoped<IOpenSearchDashboardStore, EfOpenSearchDashboardStore>();
        services.AddScoped<IOpenSearchConnectionResolver, OpenSearchConnectionResolver>();
        services.AddScoped<IOpenSearchDataGateway, OpenSearchDataGateway>();
        services.AddScoped<IOpenSearchSyncGateway, OpenSearchSyncGateway>();
        services.AddHttpClient("opensearch", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient("opensearch-sync", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        return services;
    }
}
