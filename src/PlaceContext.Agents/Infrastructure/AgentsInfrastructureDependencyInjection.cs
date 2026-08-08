using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Agents.Domain.Persistence;
using PlaceContext.Agents.Domain.Repositories;
using PlaceContext.Agents.Infrastructure.Persistence;

namespace PlaceContext.Agents;

public static class AgentsInfrastructureDependencyInjection
{
    public static IServiceCollection AddAgentsInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Agents")
            ?? configuration[$"{AgentsPersistenceOptions.SectionName}:ConnectionString"]
            ?? configuration["PlaceContext:ConnectionString"]
            ?? AgentsPersistenceOptions.DefaultConnectionString;
        services.Configure<AgentsPersistenceOptions>(options =>
            options.ConnectionString = connectionString);
        services.AddDbContext<AgentsDbContext>(options =>
        {
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Agents"));
            options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddHealthChecks().AddDbContextCheck<AgentsDbContext>("agents-database");
        services.AddScoped<IAgentsUnitOfWork>(provider => provider.GetRequiredService<AgentsDbContext>());
        services.AddScoped<IAgentsRepository, EfAgentsRepository>();
        return services;
    }
}
