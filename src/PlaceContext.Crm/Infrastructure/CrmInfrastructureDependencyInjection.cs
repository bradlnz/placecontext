using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Infrastructure.Crm;
using PlaceContext.Crm.Infrastructure.Persistence;
using PlaceContext.Crm.Infrastructure.Scheduling;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Crm;

public static class CrmInfrastructureDependencyInjection
{
    public static IServiceCollection AddCrmInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Crm")
            ?? configuration[$"{CrmPersistenceOptions.SectionName}:ConnectionString"]
            ?? configuration["PlaceContext:ConnectionString"]
            ?? CrmPersistenceOptions.DefaultConnectionString;

        services.Configure<CrmPersistenceOptions>(options =>
            options.ConnectionString = connectionString);
        services.AddDbContext<CrmDbContext>(options =>
        {
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Crm"));
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddHealthChecks().AddCheck<CrmDatabaseHealthCheck>("crm-database");
        services.AddScoped<ICrmUnitOfWork>(provider =>
            provider.GetRequiredService<CrmDbContext>());

        services.AddScoped<CrmIngestionSettingsService>();
        services.AddScoped<ICrmClientRepository, EfCrmClientRepository>();
        services.AddScoped<ICrmJobRunRepository, EfCrmJobRunRepository>();
        services.AddScoped<ICrmChainRunRepository, EfCrmChainRunRepository>();
        services.AddScoped<ICrmCommunicationRepository, EfCrmCommunicationRepository>();
        services.AddScoped<ICrmAppointmentRepository, EfCrmAppointmentRepository>();
        services.AddScoped<ICrmCalendarRepository, EfCrmCalendarRepository>();
        services.AddScoped<ICrmClientArtifactRepository, EfCrmClientArtifactRepository>();
        services.AddScoped<ICrmClientJobChainAssignmentRepository, EfCrmClientJobChainAssignmentRepository>();
        services.AddScoped<ICrmAutomationRuleRepository, EfCrmAutomationRuleRepository>();
        services.AddScoped<ICrmAutomationQueue, DbCrmAutomationQueue>();
        services.AddHostedService<CrmAutomationWorker>();
        services.AddHostedService<CrmArtifactReconciliationWorker>();
        return services;
    }
}
