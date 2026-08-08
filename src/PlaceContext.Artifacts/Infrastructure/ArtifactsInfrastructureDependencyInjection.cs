using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Ports;
using PlaceContext.Artifacts.Infrastructure.Artifacts;
using PlaceContext.Artifacts.Infrastructure.Documents;
using PlaceContext.Artifacts.Infrastructure.Persistence;
using PlaceContext.Artifacts.Infrastructure.Storage;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Artifacts;

public static class ArtifactsInfrastructureDependencyInjection
{
    public static IServiceCollection AddArtifactsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Artifacts")
            ?? configuration[$"{ArtifactsPersistenceOptions.SectionName}:ConnectionString"]
            ?? configuration["PlaceContext:ConnectionString"]
            ?? ArtifactsPersistenceOptions.DefaultConnectionString;

        services.Configure<ArtifactsPersistenceOptions>(
            configuration.GetSection(ArtifactsPersistenceOptions.SectionName));
        services.AddDbContext<ArtifactsDbContext>(options =>
        {
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Artifacts"));
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddHealthChecks()
            .AddDbContextCheck<ArtifactsDbContext>("artifacts-database");
        services.AddScoped<IArtifactsUnitOfWork>(provider =>
            provider.GetRequiredService<ArtifactsDbContext>());

        services.AddScoped<IArtifactShareTokenService, ArtifactShareTokenService>();
        services.Configure<ObjectStoreOptions>(configuration.GetSection("PlaceContext:ObjectStore"));
        services.AddSingleton<IObjectStore, S3ObjectStore>();
        services.AddScoped<IRunArtifactLinkRepository, EfRunArtifactLinkRepository>();
        services.AddSingleton<IDocumentTextExtractor, DocumentTextExtractor>();
        return services;
    }
}
