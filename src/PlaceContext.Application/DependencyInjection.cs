using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Application;

/// <summary>
/// Composition for the Application layer: the dispatcher, the pure domain services it needs, the
/// facade, and every command/query handler registered against its closed interface so the
/// reflection dispatcher can resolve it. Mirrors CodeRag's <c>AddApplication()</c>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped<IPlaceContextService, PlaceContextService>();

        // Pure domain services (no I/O) used by handlers.
        services.AddSingleton<RiskScoreCalculator>();
        services.AddSingleton<ProcessRiskScorer>();
        services.AddSingleton<TechnicalRiskScorer>();
        services.AddSingleton<ContextStalenessPolicy>();
        services.AddSingleton<DecisionTreeAssembler>();
        services.AddSingleton<TokenCostCalculator>();
        // Knowledge graph (replaces the graphify reader).
        services.AddScoped<IDecisionTreeProvider, DecisionTreeProvider>();

        // Shared risk computation (used by recompute + project creation).
        services.AddScoped<RiskAssessmentService>();

        // Trigger + event application services (event fan-out, schedule scanning, run watching).
        services.AddScoped<EventDispatchService>();
        services.AddScoped<ScheduleScanService>();
        services.AddScoped<RunStatusWatchService>();
        services.AddScoped<PostJobActionService>();
        services.AddScoped<JobRunDataRecorder>();
        services.AddScoped<DataMappingIngestionService>();
        services.AddScoped<ProjectChartService>();
        services.AddScoped<ObsidianVaultImporter>();

        // Commands.
        services.AddScoped<ICommandHandler<CreateProjectCommand, ProjectSummaryView>, CreateProjectHandler>();
        services.AddScoped<ICommandHandler<RegisterProjectCommand, ProjectSummaryView>, RegisterProjectHandler>();
        services.AddScoped<ICommandHandler<RebuildGraphCommand, ProjectSummaryView>, RebuildGraphHandler>();
        services.AddScoped<ICommandHandler<RecordActivityCommand, ActivityRecordView>, RecordActivityHandler>();
        services.AddScoped<ICommandHandler<RecomputeRiskCommand, RiskDashboardView>, RecomputeRiskHandler>();
        services.AddScoped<ICommandHandler<AddDecisionCommand, DecisionView>, AddDecisionHandler>();
        services.AddScoped<ICommandHandler<AddContextCommand, ProjectContextView>, AddContextHandler>();
        services.AddScoped<ICommandHandler<SetContextCommand, ProjectContextView>, SetContextHandler>();
        services.AddScoped<ICommandHandler<ScaffoldSkillCommand, SkillScaffoldView>, ScaffoldSkillHandler>();
        services.AddScoped<ICommandHandler<SetupHermesCommand, SkillScaffoldView>, SetupHermesHandler>();
        services.AddScoped<ICommandHandler<SetGlobalRequirementsCommand, RequirementsView>, SetGlobalRequirementsHandler>();
        services.AddScoped<ICommandHandler<SetProjectRequirementsCommand, RequirementsView>, SetProjectRequirementsHandler>();
        services.AddScoped<ICommandHandler<RecordUsageCommand, UsageEntryView>, RecordUsageHandler>();
        services.AddScoped<ICommandHandler<OnboardCommand, OnboardResultView>, OnboardHandler>();
        services.AddScoped<ICommandHandler<GenerateReportCommand, ReportView>, GenerateReportHandler>();
        services.AddScoped<ICommandHandler<DefineReportTemplateCommand, ReportTemplateView>, DefineReportTemplateHandler>();
        services.AddScoped<ICommandHandler<CreateJobCommand, JobView>, CreateJobHandler>();
        services.AddScoped<ICommandHandler<UpdateJobCommand, JobView>, UpdateJobHandler>();
        services.AddScoped<ICommandHandler<RunJobCommand, JobRunDetailView>, RunJobHandler>();
        services.AddScoped<ICommandHandler<UploadJobCodeCommand, JobView>, UploadJobCodeHandler>();
        services.AddScoped<ICommandHandler<CreateJobChainCommand, JobChainView>, CreateJobChainHandler>();
        services.AddScoped<ICommandHandler<UpdateJobChainCommand, JobChainView>, UpdateJobChainHandler>();
        services.AddScoped<ICommandHandler<DeleteJobChainCommand, bool>, DeleteJobChainHandler>();
        services.AddScoped<ICommandHandler<RunJobChainCommand, ChainRunView>, RunJobChainHandler>();
        services.AddScoped<ICommandHandler<SaveDataMappingCommand, DataMappingView>, SaveDataMappingHandler>();
        services.AddScoped<ICommandHandler<DeleteDataMappingCommand, bool>, DeleteDataMappingHandler>();
        services.AddScoped<ICommandHandler<SaveSqlChartCommand, ProjectChartView>, SaveSqlChartHandler>();
        services.AddScoped<ICommandHandler<DeleteSqlChartCommand, bool>, DeleteSqlChartHandler>();
        services.AddScoped<ICommandHandler<SaveProjectViewCommand, bool>, SaveProjectViewHandler>();
        services.AddScoped<ICommandHandler<DropProjectViewCommand, bool>, DropProjectViewHandler>();
        services.AddScoped<ICommandHandler<SaveDataEntityCommand, DataEntityView>, SaveDataEntityHandler>();
        services.AddScoped<ICommandHandler<DeleteDataEntityCommand, bool>, DeleteDataEntityHandler>();
        services.AddScoped<ICommandHandler<CreateTriggerCommand, TriggerView>, CreateTriggerHandler>();
        services.AddScoped<ICommandHandler<SetTriggerEnabledCommand, TriggerView>, SetTriggerEnabledHandler>();
        services.AddScoped<ICommandHandler<DeleteTriggerCommand, bool>, DeleteTriggerHandler>();
        services.AddScoped<ICommandHandler<DefineEventTypeCommand, EventTypeView>, DefineEventTypeHandler>();
        services.AddScoped<ICommandHandler<EmitEventCommand, EventOccurrenceView>, EmitEventHandler>();

        // Queries.
        services.AddScoped<IQueryHandler<GetProjectsQuery, IReadOnlyList<ProjectSummaryView>>, GetProjectsHandler>();
        services.AddScoped<IQueryHandler<GetProjectOverviewQuery, ProjectOverviewView>, GetProjectOverviewHandler>();
        services.AddScoped<IQueryHandler<GetTimelineQuery, ActivityTimelineView>, GetTimelineHandler>();
        services.AddScoped<IQueryHandler<GetRiskDashboardQuery, RiskDashboardView>, GetRiskDashboardHandler>();
        services.AddScoped<IQueryHandler<GetDecisionsQuery, IReadOnlyList<DecisionView>>, GetDecisionsHandler>();
        services.AddScoped<IQueryHandler<QueryGraphQuery, GraphQueryView>, QueryGraphHandler>();
        services.AddScoped<IQueryHandler<GetContextQuery, ProjectContextView>, GetContextHandler>();
        services.AddScoped<IQueryHandler<SuggestImprovementsQuery, ImprovementsView>, SuggestImprovementsHandler>();
        services.AddScoped<IQueryHandler<GetGlobalRequirementsQuery, RequirementsView>, GetGlobalRequirementsHandler>();
        services.AddScoped<IQueryHandler<GetProjectRequirementsQuery, RequirementsView>, GetProjectRequirementsHandler>();
        services.AddScoped<IQueryHandler<GetEffectiveRequirementsQuery, EffectiveRequirementsView>, GetEffectiveRequirementsHandler>();
        services.AddScoped<IQueryHandler<GetCostDashboardQuery, CostDashboardView>, GetCostDashboardHandler>();
        services.AddScoped<IQueryHandler<GetRootCostQuery, RootCostView>, GetRootCostHandler>();
        services.AddScoped<IQueryHandler<SearchQuery, SearchResultsView>, SearchHandler>();
        services.AddScoped<IQueryHandler<GetFocusQuery, FocusView>, FocusHandler>();
        services.AddScoped<IQueryHandler<GetBrainQuery, GraphVizView>, BrainHandler>();
        services.AddScoped<IQueryHandler<ListReportTemplatesQuery, IReadOnlyList<ReportTemplateView>>, ListReportTemplatesHandler>();

        // Root-level read models (redesigned portal).
        services.AddScoped<IQueryHandler<GetRootStatsQuery, RootStatsView>, GetRootStatsHandler>();
        services.AddScoped<IQueryHandler<GetRootRiskQuery, RootRiskView>, GetRootRiskHandler>();
        services.AddScoped<IQueryHandler<GetRootActivityQuery, RootActivityView>, GetRootActivityHandler>();
        services.AddScoped<IQueryHandler<GetGraphVizQuery, GraphVizView>, GetGraphVizHandler>();
        services.AddScoped<IQueryHandler<GetRecentToolCallsQuery, IReadOnlyList<ToolCallView>>, GetRecentToolCallsHandler>();
        services.AddScoped<IQueryHandler<ListJobsQuery, IReadOnlyList<JobView>>, ListJobsHandler>();
        services.AddScoped<IQueryHandler<ListJobRunsQuery, IReadOnlyList<JobRunView>>, ListJobRunsHandler>();
        services.AddScoped<IQueryHandler<ListDataMappingsQuery, IReadOnlyList<DataMappingView>>, ListDataMappingsHandler>();
        services.AddScoped<IQueryHandler<ListRecentArtifactsQuery, IReadOnlyList<ArtifactFileView>>, ListRecentArtifactsHandler>();
        services.AddScoped<IQueryHandler<ListDataEntitiesQuery, IReadOnlyList<DataEntityView>>, ListDataEntitiesHandler>();
        services.AddScoped<IQueryHandler<GetJobRunQuery, JobRunDetailView?>, GetJobRunHandler>();
        services.AddScoped<IQueryHandler<ListRecentRunReportsQuery, IReadOnlyList<RunReportView>>, ListRecentRunReportsHandler>();
        services.AddScoped<IQueryHandler<GetJobQuery, JobView?>, GetJobHandler>();
        services.AddScoped<IQueryHandler<ListTriggersQuery, IReadOnlyList<TriggerView>>, ListTriggersHandler>();
        services.AddScoped<IQueryHandler<ListJobChainsQuery, IReadOnlyList<JobChainView>>, ListJobChainsHandler>();
        services.AddScoped<IQueryHandler<ListChainRunsQuery, IReadOnlyList<ChainRunView>>, ListChainRunsHandler>();
        services.AddScoped<IQueryHandler<GetChainRunQuery, ChainRunView?>, GetChainRunHandler>();
        services.AddScoped<IQueryHandler<ListEventTypesQuery, IReadOnlyList<EventTypeView>>, ListEventTypesHandler>();
        services.AddScoped<IQueryHandler<ListEventOccurrencesQuery, IReadOnlyList<EventOccurrenceView>>, ListEventOccurrencesHandler>();
        services.AddScoped<IQueryHandler<SearchRunOutputsQuery, IReadOnlyList<RunOutputMatchView>>, SearchRunOutputsHandler>();

        // Project secrets (vault) — encrypted env injected into job runs.
        services.AddScoped<ICommandHandler<AddProjectSecretCommand, ProjectSecretView>, AddProjectSecretHandler>();
        services.AddScoped<ICommandHandler<DeleteProjectSecretCommand, bool>, DeleteProjectSecretHandler>();
        services.AddScoped<IQueryHandler<ListProjectSecretsQuery, IReadOnlyList<ProjectSecretView>>, ListProjectSecretsHandler>();
        services.AddScoped<IQueryHandler<ListRunArtifactsQuery, IReadOnlyList<RunArtifactLinkView>>, ListRunArtifactsHandler>();
        services.AddScoped<IQueryHandler<ListJobRunArtifactsQuery, IReadOnlyList<RunArtifactLinkView>>, ListJobRunArtifactsHandler>();
        services.AddScoped<ICommandHandler<ExecuteProjectDataCommand, Ports.ProjectQueryResult>, ExecuteProjectDataHandler>();
        services.AddScoped<IQueryHandler<ListProjectDataTablesQuery, IReadOnlyList<Ports.ProjectTableInfo>>, ListProjectDataTablesHandler>();
        services.AddScoped<ICommandHandler<CreateProjectTableCommand, bool>, CreateProjectTableHandler>();
        services.AddScoped<ICommandHandler<RenameProjectTableCommand, bool>, RenameProjectTableHandler>();
        services.AddScoped<ICommandHandler<DropProjectTableCommand, bool>, DropProjectTableHandler>();
        services.AddScoped<IQueryHandler<ExportProjectTableQuery, string>, ExportProjectTableHandler>();
        services.AddScoped<IQueryHandler<ListProjectTableColumnsQuery, IReadOnlyList<Ports.ProjectColumnInfo>>, ListProjectTableColumnsHandler>();
        services.AddScoped<ICommandHandler<AddProjectTableColumnCommand, bool>, AddProjectTableColumnHandler>();
        services.AddScoped<ICommandHandler<DropProjectTableColumnCommand, bool>, DropProjectTableColumnHandler>();
        services.AddScoped<ICommandHandler<GenerateProjectChartCommand, string>, GenerateProjectChartHandler>();
        services.AddScoped<IQueryHandler<ListProjectChartsQuery, IReadOnlyList<ProjectChartView>>, ListProjectChartsHandler>();
        services.AddScoped<ICommandHandler<ReceiveInboundSmsCommand, InboundSmsView>, ReceiveInboundSmsHandler>();
        services.AddScoped<IQueryHandler<ListInboundSmsQuery, IReadOnlyList<InboundSmsView>>, ListInboundSmsHandler>();

        return services;
    }
}
