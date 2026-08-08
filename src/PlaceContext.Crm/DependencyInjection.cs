using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Crm.Automation;
using PlaceContext.Crm.Services;
using PlaceContext.Jobs.Contracts.Integration;

namespace PlaceContext.Crm;

public static class DependencyInjection
{
    public static IServiceCollection AddCrmApi(this IServiceCollection services)
    {
        AddCrmOwnedServices(services);
        services.AddScoped<IQueryHandler<ListCrmClientsQuery, IReadOnlyList<CrmClientView>>, ListCrmClientsHandler>();
        services.AddScoped<IQueryHandler<ListCrmAppointmentsQuery, IReadOnlyList<CrmAppointmentView>>, ListCrmAppointmentsHandler>();
        services.AddScoped<IQueryHandler<ListCrmCalendarsQuery, IReadOnlyList<CrmCalendarView>>, ListCrmCalendarsHandler>();
        return services;
    }

    public static IServiceCollection AddCrmModule(this IServiceCollection services)
    {
        AddCrmOwnedServices(services);
        services.AddScoped<ICommandHandler<SaveCrmClientCommand, CrmClientView>, SaveCrmClientHandler>();
        services.AddScoped<ICommandHandler<MoveCrmClientCommand, CrmClientView>, MoveCrmClientHandler>();
        services.AddScoped<ICommandHandler<DeleteCrmClientCommand, bool>, DeleteCrmClientHandler>();
        services.AddScoped<ICommandHandler<ConfigureCrmClientPortalCommand, CrmClientView>, ConfigureCrmClientPortalHandler>();
        services.AddScoped<ICommandHandler<RunCrmClientAutomationCommand, CrmChainRunView>, RunCrmClientAutomationHandler>();
        services.AddScoped<ICommandHandler<AddCrmClientNoteCommand, CrmCommunicationView>, AddCrmClientNoteHandler>();
        services.AddScoped<ICommandHandler<SendCrmClientMessageCommand, CrmCommunicationView>, SendCrmClientMessageHandler>();
        services.AddScoped<ICommandHandler<CreateCrmAppointmentCommand, CrmAppointmentView>, CreateCrmAppointmentHandler>();
        services.AddScoped<ICommandHandler<DeleteCrmAppointmentCommand, bool>, DeleteCrmAppointmentHandler>();
        services.AddScoped<ICommandHandler<SaveCrmCalendarCommand, CrmCalendarView>, SaveCrmCalendarHandler>();
        services.AddScoped<ICommandHandler<DeleteCrmCalendarCommand, bool>, DeleteCrmCalendarHandler>();
        services.AddScoped<ICommandHandler<AttachCrmClientArtifactCommand, CrmClientArtifactView>, AttachCrmClientArtifactHandler>();
        services.AddScoped<ICommandHandler<RemoveCrmClientArtifactCommand, bool>, RemoveCrmClientArtifactHandler>();
        services.AddScoped<ICommandHandler<SaveCrmAutomationRuleCommand, CrmAutomationRuleView>, SaveCrmAutomationRuleHandler>();
        services.AddScoped<ICommandHandler<SetCrmAutomationEnabledCommand, CrmAutomationRuleView>, SetCrmAutomationEnabledHandler>();
        services.AddScoped<ICommandHandler<DeleteCrmAutomationRuleCommand, bool>, DeleteCrmAutomationRuleHandler>();
        services.AddScoped<ICommandHandler<SetCrmClientAssignedJobChainsCommand, IReadOnlyList<Guid>>, SetCrmClientAssignedJobChainsHandler>();
        services.AddScoped<IQueryHandler<ListCrmClientsQuery, IReadOnlyList<CrmClientView>>, ListCrmClientsHandler>();
        services.AddScoped<IQueryHandler<ListCrmClientAssignedJobChainsQuery, IReadOnlyList<Guid>>, ListCrmClientAssignedJobChainsHandler>();
        services.AddScoped<IQueryHandler<ListCrmClientChainRunsQuery, IReadOnlyList<CrmChainRunView>>, ListCrmClientChainRunsHandler>();
        services.AddScoped<IQueryHandler<ListCrmClientCommunicationsQuery, IReadOnlyList<CrmCommunicationView>>, ListCrmClientCommunicationsHandler>();
        services.AddScoped<IQueryHandler<ListCrmAppointmentsQuery, IReadOnlyList<CrmAppointmentView>>, ListCrmAppointmentsHandler>();
        services.AddScoped<IQueryHandler<ListCrmCalendarsQuery, IReadOnlyList<CrmCalendarView>>, ListCrmCalendarsHandler>();
        services.AddScoped<IQueryHandler<GetCrmCommsCapabilitiesQuery, CrmCommsCapabilitiesView>, GetCrmCommsCapabilitiesHandler>();
        services.AddScoped<IQueryHandler<ListCrmClientArtifactsQuery, IReadOnlyList<CrmClientArtifactView>>, ListCrmClientArtifactsHandler>();
        services.AddScoped<IQueryHandler<ListCrmAutomationRulesQuery, IReadOnlyList<CrmAutomationRuleView>>, ListCrmAutomationRulesHandler>();
        return services;
    }

    private static void AddCrmOwnedServices(IServiceCollection services)
    {
        services.TryAddScoped<CrmAutomationDispatcher>();
        services.TryAddScoped<CrmArtifactAssociationService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChainRunCompletionObserver>(provider =>
            provider.GetRequiredService<CrmArtifactAssociationService>()));
    }
}
