using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Observability;
using PlaceContext.Application.Ports;
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
        services.AddSingleton<ContextStalenessPolicy>();
        services.AddSingleton<DecisionTreeAssembler>();
        services.AddSingleton<TokenCostCalculator>();
        // Knowledge graph (replaces the graphify reader).
        services.AddScoped<IDecisionTreeProvider, DecisionTreeProvider>();

        // Trigger + event application services (event fan-out, schedule scanning, run watching).
        services.AddScoped<EventDispatchService>();
        services.AddScoped<ScheduleScanService>();
        services.AddScoped<RunStatusWatchService>();
        services.AddScoped<PostJobActionService>();
        services.AddScoped<JobRunDataRecorder>();
        services.AddScoped<DataMappingIngestionService>();
        services.AddScoped<EntityTagService>();
        services.AddScoped<RecordLinkService>();
        services.AddScoped<ProjectChartService>();
        services.AddScoped<ObsidianVaultImporter>();
        services.AddScoped<CrmAutomationDispatcher>();

        // Commands.
        services.AddScoped<ICommandHandler<CreateProjectCommand, ProjectSummaryView>, CreateProjectHandler>();
        services.AddScoped<ICommandHandler<RegisterProjectCommand, ProjectSummaryView>, RegisterProjectHandler>();
        services.AddScoped<ICommandHandler<RebuildGraphCommand, ProjectSummaryView>, RebuildGraphHandler>();
        services.AddScoped<ICommandHandler<RecordActivityCommand, ActivityRecordView>, RecordActivityHandler>();
        services.AddScoped<ICommandHandler<AddDecisionCommand, DecisionView>, AddDecisionHandler>();
        services.AddScoped<ICommandHandler<ScaffoldSkillCommand, SkillScaffoldView>, ScaffoldSkillHandler>();
        services.AddScoped<ICommandHandler<SetupHermesCommand, SkillScaffoldView>, SetupHermesHandler>();
        services.AddScoped<ICommandHandler<SetGlobalRequirementsCommand, RequirementsView>, SetGlobalRequirementsHandler>();
        services.AddScoped<ICommandHandler<SetProjectRequirementsCommand, RequirementsView>, SetProjectRequirementsHandler>();
        services.AddScoped<ICommandHandler<RecordUsageCommand, UsageEntryView>, RecordUsageHandler>();
        services.AddScoped<ICommandHandler<OnboardCommand, OnboardResultView>, OnboardHandler>();
        services.AddScoped<ICommandHandler<CreateJobCommand, JobView>, CreateJobHandler>();
        services.AddScoped<ICommandHandler<UpdateJobCommand, JobView>, UpdateJobHandler>();
        services.AddScoped<ICommandHandler<RunJobCommand, JobRunDetailView>, RunJobHandler>();
        services.AddScoped<ICommandHandler<SaveJobTestCaseCommand, JobTestCaseView>, SaveJobTestCaseHandler>();
        services.AddScoped<ICommandHandler<DeleteJobTestCaseCommand, bool>, DeleteJobTestCaseHandler>();
        services.AddScoped<ICommandHandler<RunJobTestCaseCommand, JobTestCaseView>, RunJobTestCaseHandler>();
        services.AddScoped<ICommandHandler<UpdateJobTestCodeCommand, JobTestCaseView>, UpdateJobTestCodeHandler>();
        services.AddScoped<ICommandHandler<SaveOpenSearchDashboardCommand, OpenSearchDashboardView>, SaveOpenSearchDashboardHandler>();
        services.AddScoped<ICommandHandler<DeleteOpenSearchDashboardCommand, bool>, DeleteOpenSearchDashboardHandler>();
        services.AddScoped<ICommandHandler<SaveSavedQueryCommand, SavedQueryRecord>, SaveSavedQueryHandler>();
        services.AddScoped<ICommandHandler<DeleteSavedQueryCommand, bool>, DeleteSavedQueryHandler>();
        services.AddScoped<ICommandHandler<TriggerOpenSearchSyncCommand, OpenSearchSyncView>, TriggerOpenSearchSyncHandler>();
        services.AddScoped<ICommandHandler<ReplayRunCommand, JobRunDetailView>, ReplayRunHandler>();
        services.AddScoped<ICommandHandler<UploadJobCodeCommand, JobView>, UploadJobCodeHandler>();
        services.AddScoped<ICommandHandler<DeleteJobCommand, bool>, DeleteJobHandler>();
        services.AddScoped<ICommandHandler<SaveCrmClientCommand, CrmClientView>, SaveCrmClientHandler>();
        services.AddScoped<ICommandHandler<MoveCrmClientCommand, CrmClientView>, MoveCrmClientHandler>();
        services.AddScoped<ICommandHandler<DeleteCrmClientCommand, bool>, DeleteCrmClientHandler>();
        services.AddScoped<ICommandHandler<ConfigureCrmClientPortalCommand, CrmClientView>, ConfigureCrmClientPortalHandler>();
        services.AddScoped<ICommandHandler<RunCrmClientAutomationCommand, CrmChainRunView>, RunCrmClientAutomationHandler>();
        services.AddScoped<CrmArtifactAssociationService>();
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
        services.AddScoped<ICommandHandler<SetCrmClientAssignedJobChainsCommand, IReadOnlyList<Guid>>,
            SetCrmClientAssignedJobChainsHandler>();
        services.AddScoped<ICommandHandler<CreateJobChainCommand, JobChainView>, CreateJobChainHandler>();
        services.AddScoped<ICommandHandler<UpdateJobChainCommand, JobChainView>, UpdateJobChainHandler>();
        services.AddScoped<ICommandHandler<DeleteJobChainCommand, bool>, DeleteJobChainHandler>();
        services.AddScoped<ICommandHandler<RunJobChainCommand, ChainRunView>, RunJobChainHandler>();
        services.AddScoped<ICommandHandler<CancelJobRunCommand, bool>, CancelJobRunHandler>();
        services.AddScoped<ICommandHandler<CancelChainRunCommand, bool>, CancelChainRunHandler>();
        services.AddScoped<ICommandHandler<ReplayJobChainCommand, ChainRunView>, ReplayJobChainHandler>();
        services.AddScoped<ICommandHandler<SaveDataMappingCommand, DataMappingView>, SaveDataMappingHandler>();
        services.AddScoped<ICommandHandler<DeleteDataMappingCommand, bool>, DeleteDataMappingHandler>();
        services.AddScoped<ICommandHandler<SaveSqlChartCommand, ProjectChartView>, SaveSqlChartHandler>();
        services.AddScoped<ICommandHandler<DeleteSqlChartCommand, bool>, DeleteSqlChartHandler>();
        services.AddScoped<ICommandHandler<SaveProjectViewCommand, bool>, SaveProjectViewHandler>();
        services.AddScoped<ICommandHandler<DropProjectViewCommand, bool>, DropProjectViewHandler>();
        services.AddScoped<ICommandHandler<SaveDataEntityCommand, DataEntityView>, SaveDataEntityHandler>();
        services.AddScoped<ICommandHandler<DeleteDataEntityCommand, bool>, DeleteDataEntityHandler>();
        services.AddScoped<ICommandHandler<CreateEntityRecordCommand, CreateEntityRecordResult>, CreateEntityRecordHandler>();
        services.AddScoped<ICommandHandler<UpdateEntityRecordCommand, int>, UpdateEntityRecordHandler>();
        services.AddScoped<ICommandHandler<DeleteEntityRecordCommand, int>, DeleteEntityRecordHandler>();
        services.AddScoped<ICommandHandler<CreateTriggerCommand, TriggerView>, CreateTriggerHandler>();
        services.AddScoped<ICommandHandler<UpdateTriggerCommand, TriggerView>, UpdateTriggerHandler>();
        services.AddScoped<ICommandHandler<SetTriggerEnabledCommand, TriggerView>, SetTriggerEnabledHandler>();
        services.AddScoped<ICommandHandler<DeleteTriggerCommand, bool>, DeleteTriggerHandler>();
        services.AddScoped<ICommandHandler<DefineEventTypeCommand, EventTypeView>, DefineEventTypeHandler>();
        services.AddScoped<ICommandHandler<EmitEventCommand, EventOccurrenceView>, EmitEventHandler>();

        // Agent chat (Phase 1).
        services.AddScoped<ICommandHandler<Features.UpdateAgentConfigCommand, Dtos.AgentConfigView>, Features.UpdateAgentConfigHandler>();
        services.AddScoped<ICommandHandler<Features.SendAgentMessageCommand, Dtos.AgentChatSessionView>, Features.SendAgentMessageHandler>();
        services.AddScoped<IQueryHandler<Features.GetAgentConfigQuery, Dtos.AgentConfigView>, Features.GetAgentConfigHandler>();
        services.AddScoped<IQueryHandler<Features.ListAgentChatSessionsQuery, IReadOnlyList<Dtos.AgentChatSessionView>>, Features.ListAgentChatSessionsHandler>();
        services.AddScoped<IQueryHandler<Features.GetAgentChatSessionQuery, Dtos.AgentChatSessionView?>, Features.GetAgentChatSessionHandler>();
        services.AddScoped<Features.AgentContextBuilder>();
        services.AddScoped<IMcpClientService, McpClientService>();

        // Job execution orchestrator (applies per-job retry policy).
        services.AddScoped<IJobRunner, JobRunner>();

        // Launchpads / Slack: unattended agent sessions driven by the [[tool:...]] protocol.
        services.AddScoped<Agents.Services.LaunchpadToolExecutor>();
        services.AddScoped<Agents.Services.AgentSessionRunner>();
        services.AddScoped<Agents.Services.SlackAgentBridge>();

        // MCP connections
        services.AddScoped<ICommandHandler<Features.CreateMcpConnectionCommand, Dtos.McpConnectionView>, Features.CreateMcpConnectionHandler>();
        services.AddScoped<ICommandHandler<Features.UpdateMcpConnectionCommand, Dtos.McpConnectionView>, Features.UpdateMcpConnectionHandler>();
        services.AddScoped<ICommandHandler<Features.DeleteMcpConnectionCommand, bool>, Features.DeleteMcpConnectionHandler>();
        services.AddScoped<ICommandHandler<Features.TestMcpConnectionCommand, Dtos.McpConnectionView>, Features.TestMcpConnectionHandler>();
        services.AddScoped<IQueryHandler<Features.ListMcpConnectionsQuery, IReadOnlyList<Dtos.McpConnectionView>>, Features.ListMcpConnectionsHandler>();

        // Chat commands
        services.AddScoped<ICommandHandler<Features.CreateChatCommandCommand, Dtos.ChatCommandView>, Features.CreateChatCommandHandler>();
        services.AddScoped<ICommandHandler<Features.UpdateChatCommandCommand, Dtos.ChatCommandView>, Features.UpdateChatCommandHandler>();
        services.AddScoped<ICommandHandler<Features.DeleteChatCommandCommand, bool>, Features.DeleteChatCommandHandler>();
        services.AddScoped<IQueryHandler<Features.ListChatCommandsQuery, IReadOnlyList<Dtos.ChatCommandView>>, Features.ListChatCommandsHandler>();

        // Queries.
        services.AddScoped<IQueryHandler<GetProjectsQuery, IReadOnlyList<ProjectSummaryView>>, GetProjectsHandler>();
        services.AddScoped<IQueryHandler<GetProjectByIdQuery, ProjectSummaryView?>, GetProjectByIdHandler>();
        services.AddScoped<IQueryHandler<GetProjectOverviewQuery, ProjectOverviewView>, GetProjectOverviewHandler>();
        services.AddScoped<IQueryHandler<GetTimelineQuery, ActivityTimelineView>, GetTimelineHandler>();
        services.AddScoped<IQueryHandler<GetDecisionsQuery, IReadOnlyList<DecisionView>>, GetDecisionsHandler>();
        services.AddScoped<IQueryHandler<QueryGraphQuery, GraphQueryView>, QueryGraphHandler>();
        services.AddScoped<IQueryHandler<SuggestImprovementsQuery, ImprovementsView>, SuggestImprovementsHandler>();
        services.AddScoped<IQueryHandler<GetGlobalRequirementsQuery, RequirementsView>, GetGlobalRequirementsHandler>();
        services.AddScoped<IQueryHandler<GetProjectRequirementsQuery, RequirementsView>, GetProjectRequirementsHandler>();
        services.AddScoped<IQueryHandler<GetEffectiveRequirementsQuery, EffectiveRequirementsView>, GetEffectiveRequirementsHandler>();
        services.AddScoped<IQueryHandler<GetCostDashboardQuery, CostDashboardView>, GetCostDashboardHandler>();
        services.AddScoped<IQueryHandler<GetRootCostQuery, RootCostView>, GetRootCostHandler>();
        services.AddScoped<IQueryHandler<SearchQuery, SearchResultsView>, SearchHandler>();
        services.AddScoped<IQueryHandler<GetFocusQuery, FocusView>, FocusHandler>();

        // Root-level read models (redesigned portal).
        services.AddScoped<IQueryHandler<GetRootStatsQuery, RootStatsView>, GetRootStatsHandler>();
        services.AddScoped<IQueryHandler<GetRootActivityQuery, RootActivityView>, GetRootActivityHandler>();
        services.AddScoped<IQueryHandler<GetGraphVizQuery, GraphVizView>, GetGraphVizHandler>();
        services.AddScoped<IQueryHandler<GetRecentToolCallsQuery, IReadOnlyList<ToolCallView>>, GetRecentToolCallsHandler>();
        services.AddScoped<IQueryHandler<ListJobsQuery, IReadOnlyList<JobView>>, ListJobsHandler>();
        services.AddScoped<IQueryHandler<ListJobRunsQuery, IReadOnlyList<JobRunView>>, ListJobRunsHandler>();
        services.AddScoped<IQueryHandler<ListCrmClientsQuery, IReadOnlyList<CrmClientView>>, ListCrmClientsHandler>();
        services.AddScoped<IQueryHandler<ListCrmClientAssignedJobChainsQuery, IReadOnlyList<Guid>>,
            ListCrmClientAssignedJobChainsHandler>();
        services.AddScoped<IQueryHandler<ListCrmClientChainRunsQuery, IReadOnlyList<CrmChainRunView>>, ListCrmClientChainRunsHandler>();
        services.AddScoped<IQueryHandler<ListCrmClientCommunicationsQuery, IReadOnlyList<CrmCommunicationView>>, ListCrmClientCommunicationsHandler>();
        services.AddScoped<IQueryHandler<ListCrmAppointmentsQuery, IReadOnlyList<CrmAppointmentView>>, ListCrmAppointmentsHandler>();
        services.AddScoped<IQueryHandler<ListCrmCalendarsQuery, IReadOnlyList<CrmCalendarView>>, ListCrmCalendarsHandler>();
        services.AddScoped<IQueryHandler<GetCrmCommsCapabilitiesQuery, CrmCommsCapabilitiesView>, GetCrmCommsCapabilitiesHandler>();
        services.AddScoped<IQueryHandler<ListCrmClientArtifactsQuery, IReadOnlyList<CrmClientArtifactView>>, ListCrmClientArtifactsHandler>();
        services.AddScoped<IQueryHandler<ListCrmAutomationRulesQuery, IReadOnlyList<CrmAutomationRuleView>>, ListCrmAutomationRulesHandler>();
        services.AddScoped<IQueryHandler<ListDataMappingsQuery, IReadOnlyList<DataMappingView>>, ListDataMappingsHandler>();
        services.AddScoped<IQueryHandler<ListRecentArtifactsQuery, IReadOnlyList<ArtifactFileView>>, ListRecentArtifactsHandler>();
        services.AddScoped<IQueryHandler<ListProjectArtifactsQuery, IReadOnlyList<ArtifactFileView>>, ListProjectArtifactsHandler>();
        services.AddScoped<ICommandHandler<DeleteArtifactCommand, bool>, DeleteArtifactHandler>();
        services.AddScoped<ICommandHandler<DeleteArtifactsCommand, int>, DeleteArtifactsHandler>();
        services.AddScoped<ICommandHandler<CreateArtifactShareCommand, ArtifactShareCreated>, CreateArtifactShareHandler>();
        services.AddScoped<ICommandHandler<RevokeArtifactShareCommand, bool>, RevokeArtifactShareHandler>();
        services.AddScoped<IQueryHandler<GetArtifactShareStatusQuery, ArtifactShareStatus?>, GetArtifactShareStatusHandler>();
        services.AddScoped<IQueryHandler<ListDataEntitiesQuery, IReadOnlyList<DataEntityView>>, ListDataEntitiesHandler>();
        services.AddScoped<IQueryHandler<TaggedRunsQuery, IReadOnlyList<Guid>>, TaggedRunsHandler>();
        services.AddScoped<IQueryHandler<EntityRunsQuery, IReadOnlyList<Guid>>, EntityRunsHandler>();
        services.AddScoped<IQueryHandler<EntityTagPairsQuery, IReadOnlyList<EntityTagPair>>, EntityTagPairsHandler>();
        services.AddScoped<IQueryHandler<GetJobRunQuery, JobRunDetailView?>, GetJobRunHandler>();
        services.AddScoped<IQueryHandler<ListJobTestCasesQuery, IReadOnlyList<JobTestCaseView>>, ListJobTestCasesHandler>();
        services.AddScoped<IQueryHandler<GetJobTestCaseQuery, JobTestCaseView?>, GetJobTestCaseHandler>();
        services.AddScoped<IQueryHandler<ListOpenSearchIndicesQuery, IReadOnlyList<OpenSearchIndexView>>, ListOpenSearchIndicesHandler>();
        services.AddScoped<IQueryHandler<ListOpenSearchFieldsQuery, IReadOnlyList<OpenSearchFieldView>>, ListOpenSearchFieldsHandler>();
        services.AddScoped<IQueryHandler<GetOpenSearchLastUpdatedQuery, OpenSearchLastUpdatedView>, GetOpenSearchLastUpdatedHandler>();
        services.AddScoped<IQueryHandler<SearchOpenSearchQuery, OpenSearchSearchView>, SearchOpenSearchHandler>();
        services.AddScoped<IQueryHandler<SearchOpenSearchSqlQuery, ProjectQueryResult>, SearchOpenSearchSqlHandler>();
        services.AddScoped<IQueryHandler<ListOpenSearchDashboardsQuery, IReadOnlyList<OpenSearchDashboardView>>, ListOpenSearchDashboardsHandler>();
        services.AddScoped<IQueryHandler<ListSavedQueriesQuery, IReadOnlyList<SavedQueryRecord>>, ListSavedQueriesHandler>();
        services.AddScoped<IQueryHandler<ListRecentRunReportsQuery, IReadOnlyList<RunReportView>>, ListRecentRunReportsHandler>();
        services.AddScoped<IQueryHandler<GetJobQuery, JobView?>, GetJobHandler>();
        services.AddScoped<IQueryHandler<ListTriggersQuery, IReadOnlyList<TriggerView>>, ListTriggersHandler>();
        services.AddScoped<IQueryHandler<GetTriggerByIdQuery, TriggerView?>, GetTriggerByIdHandler>();
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
        // OCR daemon contract (server-side storage; the daemon deploys later).
        services.AddScoped<OcrResultStorageService>();
        services.AddScoped<IQueryHandler<ListPendingOcrQuery, IReadOnlyList<PendingOcrArtifactView>>, ListPendingOcrHandler>();
        services.AddScoped<ICommandHandler<CompleteOcrCommand, bool>, CompleteOcrHandler>();
        services.AddScoped<ICommandHandler<ExecuteProjectDataCommand, Ports.ProjectQueryResult>, ExecuteProjectDataHandler>();
        services.AddScoped<IQueryHandler<ListProjectDataTablesQuery, IReadOnlyList<Ports.ProjectTableInfo>>, ListProjectDataTablesHandler>();
        services.AddScoped<IQueryHandler<QueryProjectTablePageQuery, Ports.ProjectTablePageResult>, QueryProjectTablePageHandler>();
        services.AddScoped<ICommandHandler<CreateProjectTableCommand, bool>, CreateProjectTableHandler>();
        services.AddScoped<ICommandHandler<ImportCsvToProjectTableCommand, ImportCsvResult>, ImportCsvToProjectTableHandler>();
        services.AddScoped<ICommandHandler<MaterializeTableIndexCommand, MaterializeTableIndexResult>, MaterializeTableIndexHandler>();
        services.AddScoped<ICommandHandler<RenameProjectTableCommand, bool>, RenameProjectTableHandler>();
        services.AddScoped<ICommandHandler<DropProjectTableCommand, bool>, DropProjectTableHandler>();
        services.AddScoped<IQueryHandler<ExportProjectTableQuery, string>, ExportProjectTableHandler>();
        services.AddScoped<IQueryHandler<ListProjectTableColumnsQuery, IReadOnlyList<Ports.ProjectColumnInfo>>, ListProjectTableColumnsHandler>();
        services.AddScoped<ICommandHandler<AddProjectTableColumnCommand, bool>, AddProjectTableColumnHandler>();
        services.AddScoped<ICommandHandler<DropProjectTableColumnCommand, bool>, DropProjectTableColumnHandler>();
        services.AddScoped<ICommandHandler<GenerateProjectChartCommand, string>, GenerateProjectChartHandler>();
        services.AddScoped<IQueryHandler<ListProjectChartsQuery, IReadOnlyList<ProjectChartView>>, ListProjectChartsHandler>();
        // Backup/restore (tenant settings + job definitions → a portable manifest).
        services.AddScoped<IQueryHandler<ExportManifestQuery, BackupManifest>, ExportManifestHandler>();
        services.AddScoped<ICommandHandler<ImportManifestCommand, ImportResultView>, ImportManifestHandler>();

        // Granular RBAC: a member's permission matrix (role defaults + tenant-scoped overrides).
        services.AddScoped<IQueryHandler<GetUserPermissionsQuery, UserPermissionsView>, GetUserPermissionsHandler>();
        services.AddScoped<ICommandHandler<SetUserPermissionOverrideCommand, UserPermissionsView>, SetUserPermissionOverrideHandler>();

        // Editable roles: list/create/update/delete role definitions (the Access "Roles & permissions" UI).
        services.AddScoped<IQueryHandler<ListRolesQuery, IReadOnlyList<RoleView>>, ListRolesHandler>();
        services.AddScoped<ICommandHandler<CreateRoleCommand, RoleView>, CreateRoleHandler>();
        services.AddScoped<ICommandHandler<UpdateRolePermissionsCommand, RoleView>, UpdateRolePermissionsHandler>();
        services.AddScoped<ICommandHandler<DeleteRoleCommand, bool>, DeleteRoleHandler>();

        // Cluster page: node inventory + promote-to-master + join material (Tailscale fleet).
        services.AddScoped<IQueryHandler<PlaceContext.Application.Cluster.GetClusterInfoQuery, Ports.ClusterInfo>,
            PlaceContext.Application.Cluster.GetClusterInfoHandler>();
        services.AddScoped<ICommandHandler<PlaceContext.Application.Cluster.PromoteNodeToMasterCommand, Ports.PromoteMasterResult>,
            PlaceContext.Application.Cluster.PromoteNodeToMasterHandler>();
        services.AddScoped<IQueryHandler<PlaceContext.Application.Cluster.GetClusterJoinMaterialQuery, Ports.ClusterJoinMaterial?>,
            PlaceContext.Application.Cluster.GetClusterJoinMaterialHandler>();
        services.AddScoped<ICommandHandler<PlaceContext.Application.Cluster.LaunchClusterAgentCommand, PlaceContext.Application.Cluster.LaunchAgentResult>,
            PlaceContext.Application.Cluster.LaunchClusterAgentHandler>();
        services.AddScoped<ICommandHandler<PlaceContext.Application.Cluster.CreateAgentJoinTokenCommand, string>,
            PlaceContext.Application.Cluster.CreateAgentJoinTokenHandler>();
        services.AddScoped<IQueryHandler<GetJobTelemetrySnapshotQuery, Ports.JobTelemetrySnapshot>, GetJobTelemetrySnapshotHandler>();
        services.AddScoped<IQueryHandler<ListRecentJobRunTelemetryQuery, IReadOnlyList<Ports.JobRunTelemetry>>, ListRecentJobRunTelemetryHandler>();
        services.AddScoped<IQueryHandler<ListJobRunTelemetryQuery, IReadOnlyList<Ports.JobRunTelemetry>>, ListJobRunTelemetryHandler>();
        services.AddScoped<IQueryHandler<ListRecentChainRunTelemetryQuery, IReadOnlyList<Ports.ChainRunTelemetry>>, ListRecentChainRunTelemetryHandler>();
        services.AddScoped<IQueryHandler<ListRecentChainRunsQuery, IReadOnlyList<ChainRunReportView>>, ListRecentChainRunsHandler>();

        return services;
    }
}
