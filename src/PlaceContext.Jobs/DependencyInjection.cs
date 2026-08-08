using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Jobs;

public static class DependencyInjection
{
    public static IServiceCollection AddJobsApi(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<ListJobsQuery, IReadOnlyList<JobView>>, ListJobsHandler>();
        services.AddScoped<IQueryHandler<GetJobQuery, JobView?>, GetJobHandler>();
        services.AddScoped<ICommandHandler<UpdateJobCommand, JobView>, UpdateJobHandler>();
        services.AddScoped<ICommandHandler<DeleteJobCommand, bool>, DeleteJobHandler>();
        services.AddScoped<IQueryHandler<ListJobChainsQuery, IReadOnlyList<JobChainView>>, ListJobChainsHandler>();
        services.AddScoped<IQueryHandler<ListTriggersQuery, IReadOnlyList<TriggerView>>, ListTriggersHandler>();
        services.AddScoped<IQueryHandler<GetTriggerByIdQuery, TriggerView?>, GetTriggerByIdHandler>();
        services.AddScoped<ICommandHandler<UpdateTriggerCommand, TriggerView>, UpdateTriggerHandler>();
        services.AddScoped<ICommandHandler<DeleteTriggerCommand, bool>, DeleteTriggerHandler>();
        services.AddScoped<ICommandHandler<CancelJobRunCommand, bool>, CancelJobRunHandler>();
        services.AddScoped<ICommandHandler<CancelChainRunCommand, bool>, CancelChainRunHandler>();
        services.AddScoped<ICommandHandler<RunJobChainCommand, ChainRunView>, RunJobChainHandler>();
        services.AddScoped<ICommandHandler<ReplayJobChainCommand, ChainRunView>, ReplayJobChainHandler>();
        return services;
    }

    public static IServiceCollection AddJobsModule(this IServiceCollection services)
    {
        services.AddScoped<RunStatusWatchService>();
        services.AddScoped<PostJobActionService>();
        services.AddScoped<JobRunDataRecorder>();
        services.AddScoped<EventDispatchService>();
        services.AddScoped<IEventDispatcher>(provider => provider.GetRequiredService<EventDispatchService>());
        services.AddScoped<ScheduleScanService>();
        services.AddScoped<ICommandHandler<DefineEventTypeCommand, EventTypeView>, DefineEventTypeHandler>();
        services.AddScoped<ICommandHandler<EmitEventCommand, EventOccurrenceView>, EmitEventHandler>();
        services.AddScoped<IQueryHandler<ListEventTypesQuery, IReadOnlyList<EventTypeView>>, ListEventTypesHandler>();
        services.AddScoped<IQueryHandler<ListEventOccurrencesQuery, IReadOnlyList<EventOccurrenceView>>, ListEventOccurrencesHandler>();
        services.AddScoped<IJobRunner, JobRunner>();
        services.AddScoped<ICommandHandler<CreateJobCommand, JobView>, CreateJobHandler>();
        services.AddScoped<ICommandHandler<UpdateJobCommand, JobView>, UpdateJobHandler>();
        services.AddScoped<ICommandHandler<RunJobCommand, JobRunDetailView>, RunJobHandler>();
        services.AddScoped<ICommandHandler<SaveJobTestCaseCommand, JobTestCaseView>, SaveJobTestCaseHandler>();
        services.AddScoped<ICommandHandler<DeleteJobTestCaseCommand, bool>, DeleteJobTestCaseHandler>();
        services.AddScoped<ICommandHandler<RunJobTestCaseCommand, JobTestCaseView>, RunJobTestCaseHandler>();
        services.AddScoped<ICommandHandler<UpdateJobTestCodeCommand, JobTestCaseView>, UpdateJobTestCodeHandler>();
        services.AddScoped<ICommandHandler<ReplayRunCommand, JobRunDetailView>, ReplayRunHandler>();
        services.AddScoped<ICommandHandler<UploadJobCodeCommand, JobView>, UploadJobCodeHandler>();
        services.AddScoped<ICommandHandler<DeleteJobCommand, bool>, DeleteJobHandler>();
        services.AddScoped<ICommandHandler<CreateJobChainCommand, JobChainView>, CreateJobChainHandler>();
        services.AddScoped<ICommandHandler<UpdateJobChainCommand, JobChainView>, UpdateJobChainHandler>();
        services.AddScoped<ICommandHandler<DeleteJobChainCommand, bool>, DeleteJobChainHandler>();
        services.AddScoped<ICommandHandler<RunJobChainCommand, ChainRunView>, RunJobChainHandler>();
        services.AddScoped<ICommandHandler<CancelJobRunCommand, bool>, CancelJobRunHandler>();
        services.AddScoped<ICommandHandler<CancelChainRunCommand, bool>, CancelChainRunHandler>();
        services.AddScoped<ICommandHandler<ReplayJobChainCommand, ChainRunView>, ReplayJobChainHandler>();
        services.AddScoped<ICommandHandler<CreateTriggerCommand, TriggerView>, CreateTriggerHandler>();
        services.AddScoped<ICommandHandler<UpdateTriggerCommand, TriggerView>, UpdateTriggerHandler>();
        services.AddScoped<ICommandHandler<SetTriggerEnabledCommand, TriggerView>, SetTriggerEnabledHandler>();
        services.AddScoped<ICommandHandler<DeleteTriggerCommand, bool>, DeleteTriggerHandler>();
        services.AddScoped<IQueryHandler<ListJobsQuery, IReadOnlyList<JobView>>, ListJobsHandler>();
        services.AddScoped<IQueryHandler<ListJobRunsQuery, IReadOnlyList<JobRunView>>, ListJobRunsHandler>();
        services.AddScoped<IQueryHandler<GetJobRunQuery, JobRunDetailView?>, GetJobRunHandler>();
        services.AddScoped<IQueryHandler<ListJobTestCasesQuery, IReadOnlyList<JobTestCaseView>>, ListJobTestCasesHandler>();
        services.AddScoped<IQueryHandler<GetJobTestCaseQuery, JobTestCaseView?>, GetJobTestCaseHandler>();
        services.AddScoped<IQueryHandler<GetJobQuery, JobView?>, GetJobHandler>();
        services.AddScoped<IQueryHandler<ListTriggersQuery, IReadOnlyList<TriggerView>>, ListTriggersHandler>();
        services.AddScoped<IQueryHandler<GetTriggerByIdQuery, TriggerView?>, GetTriggerByIdHandler>();
        services.AddScoped<IQueryHandler<ListJobChainsQuery, IReadOnlyList<JobChainView>>, ListJobChainsHandler>();
        services.AddScoped<IQueryHandler<ListChainRunsQuery, IReadOnlyList<ChainRunView>>, ListChainRunsHandler>();
        services.AddScoped<IQueryHandler<GetChainRunQuery, ChainRunView?>, GetChainRunHandler>();
        services.AddScoped<IQueryHandler<ListRecentRunReportsQuery, IReadOnlyList<RunReportView>>, ListRecentRunReportsHandler>();
        services.AddScoped<IQueryHandler<ListRecentChainRunsQuery, IReadOnlyList<ChainRunReportView>>, ListRecentChainRunsHandler>();
        return services;
    }
}
