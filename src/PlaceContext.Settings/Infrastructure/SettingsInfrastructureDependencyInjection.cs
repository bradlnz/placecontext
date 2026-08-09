using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlaceContext.Settings.Infrastructure.Integration;
using PlaceContext.Settings.Infrastructure.Persistence;
using PlaceContext.Settings.Integration;
using PlaceContext.Settings.Persistence;

namespace PlaceContext.Settings;

public static class SettingsInfrastructureDependencyInjection
{
    public static IServiceCollection AddSettingsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton(configuration);
        var connectionString = configuration.GetConnectionString("Settings")
            ?? configuration["PlaceContext:Settings:ConnectionString"]
            ?? configuration["PlaceContext:ConnectionString"]
            ?? "Host=localhost;Port=5432;Database=placecontext;Username=placecontext;Password=placecontext";
        services.AddDbContext<SettingsDbContext>(options => options.UseNpgsql(connectionString)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddHealthChecks().AddDbContextCheck<SettingsDbContext>("settings-database");
        services.AddHttpClient();
        services.AddScoped<ISettingsStore, EfSettingsStore>();
        services.AddScoped<ISettingsConnectionsClient, HttpSettingsConnectionsClient>();
        services.AddScoped<ISettingsBackupClient, HttpSettingsBackupClient>();
        return services;
    }
}
