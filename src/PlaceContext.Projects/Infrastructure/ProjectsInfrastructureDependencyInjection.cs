using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Projects.Infrastructure.Files;
using PlaceContext.Projects.Infrastructure.Git;
using PlaceContext.Projects.Infrastructure.Integration;
using PlaceContext.Projects.Infrastructure.Persistence;
using PlaceContext.Projects.Infrastructure.Security;
using PlaceContext.Projects.Infrastructure.Tenancy;
using PlaceContext.Projects.Integration;

namespace PlaceContext.Projects;

public static class ProjectsInfrastructureDependencyInjection
{
    public static IServiceCollection AddProjectsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Projects")
            ?? configuration[$"{ProjectsPersistenceOptions.SectionName}:ConnectionString"]
            ?? configuration["PlaceContext:ConnectionString"]
            ?? ProjectsPersistenceOptions.DefaultConnectionString;

        services.Configure<ProjectsPersistenceOptions>(options =>
            options.ConnectionString = connectionString);
        services.AddDbContext<ProjectsDbContext>(options =>
        {
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Projects"));
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddHealthChecks().AddCheck<ProjectsDatabaseHealthCheck>("projects-database");
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<ProjectsDbContext>());
        services.AddScoped<IProjectRepository, EfProjectRepository>();
        services.AddScoped<IActivityLogRepository, EfActivityLogRepository>();
        services.AddScoped<IDecisionRepository, EfDecisionRepository>();
        services.AddScoped<IRequirementsRepository, EfRequirementsRepository>();

        services.AddDataProtection().SetApplicationName("placecontext");
        services.AddSingleton<IDataEncryptor, ProjectsDataProtectionEncryptor>();
        services.AddSingleton<IGitPort, CliGitAdapter>();
        services.AddSingleton<IRepoFiles, FileRepoFiles>();

        services.AddHttpClient();
        services.AddScoped<IRequestTenantResolver, HttpIdentityTenantResolver>();
        services.AddScoped<IProjectGraphClient, HttpProjectGraphClient>();
        services.AddScoped<IProjectEventPublisher, HttpProjectEventPublisher>();
        return services;
    }
}
