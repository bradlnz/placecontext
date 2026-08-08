using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Infrastructure.Crm;
using PlaceContext.Crm.Infrastructure.Persistence;
using PlaceContext.Crm.Infrastructure.Scheduling;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Crm;

public static class CrmInfrastructureDependencyInjection
{
    public static IServiceCollection AddCrmInfrastructure(this IServiceCollection services)
    {
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
