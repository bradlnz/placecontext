using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PlaceContext.Communications.Infrastructure.Integration;
using PlaceContext.Communications.Infrastructure.Persistence;
using PlaceContext.Communications.Infrastructure.Providers;

namespace PlaceContext.Communications;

public static class CommunicationsInfrastructureDependencyInjection
{
    public static IServiceCollection AddCommunicationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Communications")
            ?? configuration[$"{CommunicationsPersistenceOptions.SectionName}:ConnectionString"]
            ?? configuration["PlaceContext:ConnectionString"]
            ?? CommunicationsPersistenceOptions.DefaultConnectionString;

        services.Configure<CommunicationsPersistenceOptions>(
            configuration.GetSection(CommunicationsPersistenceOptions.SectionName));
        services.AddDbContext<CommunicationsDbContext>(options =>
        {
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Communications"));
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddHttpClient();
        services.AddScoped<ICommunicationProviderService, CommunicationProviderService>();
        services.AddScoped<ICommunicationSender, CommunicationSender>();
        services.AddScoped<ICommunicationVaultClient, HttpCommunicationVaultClient>();
        services.AddScoped<ICommunicationDirectoryClient, HttpCommunicationDirectoryClient>();
        services.AddHealthChecks()
            .AddDbContextCheck<CommunicationsDbContext>("communications-database");
        return services;
    }
}
