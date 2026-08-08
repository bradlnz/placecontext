using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Search.Infrastructure.OpenSearch;
using PlaceContext.Search.Infrastructure.Persistence;
using PlaceContext.Search.Infrastructure.Security;

namespace PlaceContext.Search;

public static class SearchInfrastructureDependencyInjection
{
    public static IServiceCollection AddSearchInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Search")
            ?? configuration[$"{SearchPersistenceOptions.SectionName}:ConnectionString"]
            ?? configuration["PlaceContext:ConnectionString"]
            ?? SearchPersistenceOptions.DefaultConnectionString;

        services.Configure<SearchPersistenceOptions>(
            configuration.GetSection(SearchPersistenceOptions.SectionName));
        services.AddDbContext<SearchDbContext>(options =>
        {
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Search"));
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddHealthChecks()
            .AddDbContextCheck<SearchDbContext>("search-database");
        services.AddScoped<ISearchUnitOfWork>(provider =>
            provider.GetRequiredService<SearchDbContext>());

        services.Configure<SearchDataProtectionOptions>(
            configuration.GetSection(SearchDataProtectionOptions.SectionName));
        var dataProtection = services.AddDataProtection().SetApplicationName("placecontext");
        var keyDirectory = configuration[$"{SearchDataProtectionOptions.SectionName}:KeyDirectory"];
        if (!string.IsNullOrWhiteSpace(keyDirectory))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
        services.TryAddSingleton<IDataEncryptor, SearchDataProtectionEncryptor>();

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
