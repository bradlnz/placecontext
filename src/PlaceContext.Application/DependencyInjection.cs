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
    public static IServiceCollection AddApplicationCore(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();
        return services;
    }

    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddApplicationCore();
        services.AddScoped<IPlaceContextService, PlaceContextService>();

        // Pure domain services (no I/O) used by handlers.
        services.AddSingleton<ContextStalenessPolicy>();
        services.AddSingleton<DecisionTreeAssembler>();
        services.AddScoped<DecisionTreeProvider>();
        services.AddScoped<IUncachedDecisionTreeProvider>(provider =>
            provider.GetRequiredService<DecisionTreeProvider>());
        services.AddScoped<IDecisionTreeProvider>(provider =>
            provider.GetRequiredService<DecisionTreeProvider>());

        // Trigger + event application services (event fan-out, schedule scanning, run watching).
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
        services.AddScoped<ICommandHandler<OnboardCommand, OnboardResultView>, OnboardHandler>();
        services.AddScoped<ICommandHandler<SaveSavedQueryCommand, SavedQueryRecord>, SaveSavedQueryHandler>();
        services.AddScoped<ICommandHandler<DeleteSavedQueryCommand, bool>, DeleteSavedQueryHandler>();
        services.AddScoped<CrmArtifactAssociationService>();

        services.AddScoped<IMcpClientService, McpClientService>();

        // Job execution orchestrator (applies per-job retry policy).
        services.AddScoped<IJobRunner, JobRunner>();

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
        services.AddScoped<IQueryHandler<GetFocusQuery, FocusView>, FocusHandler>();

        // Root-level read models (redesigned portal).
        services.AddScoped<IQueryHandler<GetRootStatsQuery, RootStatsView>, GetRootStatsHandler>();
        services.AddScoped<IQueryHandler<GetRootActivityQuery, RootActivityView>, GetRootActivityHandler>();
        services.AddScoped<IQueryHandler<GetGraphVizQuery, GraphVizView>, GetGraphVizHandler>();
        services.AddScoped<IQueryHandler<GetRecentToolCallsQuery, IReadOnlyList<ToolCallView>>, GetRecentToolCallsHandler>();
        services.AddScoped<IQueryHandler<ListSavedQueriesQuery, IReadOnlyList<SavedQueryRecord>>, ListSavedQueriesHandler>();
        services.AddScoped<IQueryHandler<ListEventTypesQuery, IReadOnlyList<EventTypeView>>, ListEventTypesHandler>();
        services.AddScoped<IQueryHandler<ListEventOccurrencesQuery, IReadOnlyList<EventOccurrenceView>>, ListEventOccurrencesHandler>();

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

        return services;
    }
}
