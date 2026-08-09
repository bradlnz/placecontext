using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Analytics;
using PlaceContext.Data.Infrastructure.Analytics;
using PlaceContext.Data.Infrastructure.Persistence;
using PlaceContext.Data.Infrastructure.ProjectData;
using PlaceContext.Data.Infrastructure.Security;
using PlaceContext.Data.Infrastructure.Integration;
using PlaceContext.Data.Integration;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Data;

public static class DataInfrastructureDependencyInjection
{
    public static IServiceCollection AddDataInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Data")
            ?? configuration[$"{DataPersistenceOptions.SectionName}:ConnectionString"]
            ?? configuration["PlaceContext:ConnectionString"]
            ?? DataPersistenceOptions.DefaultConnectionString;

        services.Configure<DataPersistenceOptions>(options =>
            options.ConnectionString = connectionString);
        services.AddDbContext<DataDbContext>(options =>
        {
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Data"));
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddHealthChecks()
            .AddDbContextCheck<DataDbContext>("data-database");
        services.AddScoped<IDataUnitOfWork>(provider =>
            provider.GetRequiredService<DataDbContext>());

        services.Configure<DataDataProtectionOptions>(
            configuration.GetSection(DataDataProtectionOptions.SectionName));
        var dataProtection = services.AddDataProtection().SetApplicationName("placecontext");
        var keyDirectory = configuration[$"{DataDataProtectionOptions.SectionName}:KeyDirectory"];
        if (!string.IsNullOrWhiteSpace(keyDirectory))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
        services.TryAddSingleton<IDataEncryptor, DataDataProtectionEncryptor>();

        services.AddScoped<IDataMappingRepository, EfDataMappingRepository>();
        services.AddScoped<IDataEntityRepository, EfDataEntityRepository>();
        services.AddScoped<IEntityTagStore, EfEntityTagStore>();
        services.AddScoped<IRecordLinkStore, EfRecordLinkStore>();
        services.AddScoped<IProjectChartRepository, EfProjectChartRepository>();
        services.AddScoped<ISavedQueryStore, EfSavedQueryStore>();
        services.AddScoped<IDataJobsClient, HttpDataJobsClient>();
        services.AddScoped<IDataProjectsClient, HttpDataProjectsClient>();
        services.AddScoped<IDataSearchClient, HttpDataSearchClient>();
        services.AddScoped<IDataVaultClient, HttpDataVaultClient>();
        services.TryAddSingleton<AnalyticsRefreshQueue>();
        services.TryAddSingleton<IBackgroundOperationNotifier, LoggingAnalyticsOperationNotifier>();
        services.AddHostedService<AnalyticsWorkerService>();
        services.AddHttpClient();
        services.AddScoped<IProjectDatabaseConnectionResolver, ProjectDatabaseConnectionResolver>();
        services.AddScoped<IProjectDataStore, NpgsqlProjectDataStore>();
        return services;
    }
}
