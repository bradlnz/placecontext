using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;

namespace PlaceContext.Application;

public sealed class PlaceContextService : IPlaceContextService
{
    private readonly IDispatcher _dispatcher;
    private readonly IJobRunner _jobRunner;
    private readonly RecordLinkService _links;
    private readonly IRecordLinkStore _linkStore;

    public PlaceContextService(IDispatcher dispatcher, IJobRunner jobRunner, RecordLinkService links, IRecordLinkStore linkStore)
    {
        _dispatcher = dispatcher;
        _jobRunner = jobRunner;
        _links = links;
        _linkStore = linkStore;
    }

    public Task<ProjectSummaryView> CreateProjectAsync(string path, string? name, CancellationToken ct = default)
        => _dispatcher.Send(new CreateProjectCommand(path, name), ct);

    public Task<OnboardResultView> OnboardAsync(string path, string? name, string agent, int backfillLimit, CancellationToken ct = default)
        => _dispatcher.Send(new OnboardCommand(path, name, agent, backfillLimit), ct);

    public Task<ProjectSummaryView> RegisterProjectAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Send(new RegisterProjectCommand(projectId), ct);

    public Task<ProjectSummaryView> RebuildGraphAsync(Guid projectId, bool incremental = true, CancellationToken ct = default)
        => _dispatcher.Send(new RebuildGraphCommand(projectId, incremental), ct);

    public Task<ActivityRecordView> RecordActivityAsync(RecordActivityCommand command, CancellationToken ct = default)
        => _dispatcher.Send(command, ct);

    public Task<DecisionView> AddDecisionAsync(Guid projectId, string question, string choice, string? rationale, CancellationToken ct = default)
        => _dispatcher.Send(new AddDecisionCommand(projectId, question, choice, rationale), ct);

    public Task<IReadOnlyList<ProjectSummaryView>> GetProjectsAsync(CancellationToken ct = default)
        => _dispatcher.Query(new GetProjectsQuery(), ct);

    public Task<ProjectSummaryView?> GetProjectByIdAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetProjectByIdQuery(projectId), ct);

    public Task<ProjectOverviewView> GetProjectOverviewAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetProjectOverviewQuery(projectId), ct);

    public Task<ActivityTimelineView> GetTimelineAsync(Guid projectId, int take = 50, CancellationToken ct = default)
        => _dispatcher.Query(new GetTimelineQuery(projectId, take), ct);

    public Task<IReadOnlyList<DecisionView>> GetDecisionsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetDecisionsQuery(projectId), ct);

    public Task<GraphQueryView> QueryGraphAsync(Guid projectId, string question, CancellationToken ct = default)
        => _dispatcher.Query(new QueryGraphQuery(projectId, question), ct);

    public Task<ProjectSecretView> AddProjectSecretAsync(Guid projectId, string name, string value, CancellationToken ct = default)
        => _dispatcher.Send(new AddProjectSecretCommand(projectId, name, value), ct);

    public Task<bool> DeleteProjectSecretAsync(Guid projectId, string name, CancellationToken ct = default)
        => _dispatcher.Send(new DeleteProjectSecretCommand(projectId, name), ct);

    public Task<IReadOnlyList<ProjectSecretView>> ListProjectSecretsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new ListProjectSecretsQuery(projectId), ct);

    public Task<RequirementsView> GetGlobalRequirementsAsync(CancellationToken ct = default)
        => _dispatcher.Query(new GetGlobalRequirementsQuery(), ct);

    public Task<RequirementsView> SetGlobalRequirementsAsync(string markdown, CancellationToken ct = default)
        => _dispatcher.Send(new SetGlobalRequirementsCommand(markdown), ct);

    public Task<RequirementsView> GetProjectRequirementsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetProjectRequirementsQuery(projectId), ct);

    public Task<RequirementsView> SetProjectRequirementsAsync(Guid projectId, string markdown, CancellationToken ct = default)
        => _dispatcher.Send(new SetProjectRequirementsCommand(projectId, markdown), ct);

    public Task<EffectiveRequirementsView> GetEffectiveRequirementsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetEffectiveRequirementsQuery(projectId), ct);

    public Task<UsageEntryView> RecordUsageAsync(Guid projectId, string model, long inputTokens, long outputTokens, string? description, CancellationToken ct = default)
        => _dispatcher.Send(new RecordUsageCommand(projectId, model, inputTokens, outputTokens, description), ct);

    public Task<CostDashboardView> GetCostDashboardAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetCostDashboardQuery(projectId), ct);

    public Task<RootCostView> GetRootCostAsync(CancellationToken ct = default)
        => _dispatcher.Query(new GetRootCostQuery(), ct);

    public Task<SearchResultsView> SearchAsync(string term, CancellationToken ct = default)
        => _dispatcher.Query(new SearchQuery(term), ct);

    public Task<FocusView> GetFocusAsync(CancellationToken ct = default)
        => _dispatcher.Query(new GetFocusQuery(), ct);

    public Task<ImprovementsView> SuggestImprovementsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new SuggestImprovementsQuery(projectId), ct);

    public Task<SkillScaffoldView> ScaffoldSkillAsync(Guid projectId, string skillName, string? description, CancellationToken ct = default)
        => _dispatcher.Send(new ScaffoldSkillCommand(projectId, skillName, description), ct);

    public Task<SkillScaffoldView> SetupHermesAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Send(new SetupHermesCommand(projectId), ct);

    public Task<RootStatsView> GetRootStatsAsync(CancellationToken ct = default)
        => _dispatcher.Query(new GetRootStatsQuery(), ct);

    public Task<RootActivityView> GetRootActivityAsync(int take = 40, CancellationToken ct = default)
        => _dispatcher.Query(new GetRootActivityQuery(take), ct);

    public Task<GraphVizView> GetGraphVizAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetGraphVizQuery(projectId), ct);

    public Task<IReadOnlyList<ToolCallView>> GetRecentToolCallsAsync(int take = 100, CancellationToken ct = default)
        => _dispatcher.Query(new GetRecentToolCallsQuery(take), ct);

    public Task<JobView> CreateJobAsync(CreateJobCommand command, CancellationToken ct = default)
        => _dispatcher.Send(command, ct);

    public Task<JobView> UpdateJobAsync(UpdateJobCommand command, CancellationToken ct = default)
        => _dispatcher.Send(command, ct);

    public Task<JobRunDetailView> RunJobAsync(Guid jobId, string? inputPayload = null, Guid? runId = null, CancellationToken ct = default)
        => _jobRunner.RunAsync(jobId, inputPayload, runId, ct: ct);

    public Task<JobRunDetailView> ReplayRunAsync(Guid runId, Guid? newRunId = null, CancellationToken ct = default)
        => _dispatcher.Send(new ReplayRunCommand(runId, newRunId), ct);

    public Task<IReadOnlyList<JobView>> ListJobsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new ListJobsQuery(projectId), ct);

    public Task<IReadOnlyList<JobRunView>> ListJobRunsAsync(Guid jobId, CancellationToken ct = default)
        => _dispatcher.Query(new ListJobRunsQuery(jobId), ct);

    public Task<JobRunDetailView?> GetJobRunAsync(Guid runId, CancellationToken ct = default)
        => _dispatcher.Query(new GetJobRunQuery(runId), ct);

    public Task<IReadOnlyList<RunArtifactLinkView>> ListRunArtifactsAsync(Guid runId, CancellationToken ct = default)
        => _dispatcher.Query(new ListRunArtifactsQuery(runId), ct);

    public Task<IReadOnlyList<RunArtifactLinkView>> ListJobRunArtifactsAsync(Guid jobId, CancellationToken ct = default)
        => _dispatcher.Query(new ListJobRunArtifactsQuery(jobId), ct);

    public Task<bool> DeleteJobAsync(Guid jobId, CancellationToken ct = default)
        => _dispatcher.Send(new DeleteJobCommand(jobId), ct);

    public Task<IReadOnlyList<ArtifactFileView>> ListRecentArtifactsAsync(int take = 100, CancellationToken ct = default)
        => _dispatcher.Query(new ListRecentArtifactsQuery(take), ct);

    public Task<IReadOnlyList<ArtifactFileView>> ListProjectArtifactsAsync(Guid projectId, int take = 2000, string? search = null, CancellationToken ct = default)
        => _dispatcher.Query(new ListProjectArtifactsQuery(projectId, take, search), ct);

    public Task<bool> DeleteArtifactAsync(Guid artifactId, CancellationToken ct = default)
        => _dispatcher.Send(new DeleteArtifactCommand(artifactId), ct);

    public Task<int> DeleteArtifactsAsync(IReadOnlyList<Guid> artifactIds, CancellationToken ct = default)
        => _dispatcher.Send(new DeleteArtifactsCommand(artifactIds), ct);

    public Task<JobChainView> CreateJobChainAsync(Guid projectId, string name, string? description, IReadOnlyList<Guid> stepJobIds, IReadOnlyList<IReadOnlyList<Guid>>? stages = null, CancellationToken ct = default)
        => _dispatcher.Send(new CreateJobChainCommand(projectId, name, description, stepJobIds, stages), ct);

    public Task<JobChainView> UpdateJobChainAsync(Guid chainId, string name, string? description, IReadOnlyList<Guid> stepJobIds, IReadOnlyList<IReadOnlyList<Guid>>? stages = null, CancellationToken ct = default)
        => _dispatcher.Send(new UpdateJobChainCommand(chainId, name, description, stepJobIds, stages), ct);

    public Task<bool> DeleteJobChainAsync(Guid chainId, CancellationToken ct = default)
        => _dispatcher.Send(new DeleteJobChainCommand(chainId), ct);

    public Task<IReadOnlyList<JobChainView>> ListJobChainsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new ListJobChainsQuery(projectId), ct);

    public Task<ChainRunView> RunJobChainAsync(Guid chainId, string? inputPayload = null, Guid? chainRunId = null, IReadOnlyDictionary<int, string>? stepPayloadOverrides = null, CancellationToken ct = default)
        => _dispatcher.Send(new RunJobChainCommand(chainId, inputPayload, chainRunId, stepPayloadOverrides), ct);

    public Task<ChainRunView> ReplayJobChainAsync(ReplayJobChainCommand command, CancellationToken ct = default)
        => _dispatcher.Send(command, ct);

    public Task<IReadOnlyList<ChainRunView>> ListChainRunsAsync(Guid chainId, int take = 20, CancellationToken ct = default)
        => _dispatcher.Query(new ListChainRunsQuery(chainId, take), ct);

    public Task<ChainRunView?> GetChainRunAsync(Guid chainRunId, CancellationToken ct = default)
        => _dispatcher.Query(new GetChainRunQuery(chainRunId), ct);

    public Task<IReadOnlyList<ChainRunReportView>> ListRecentChainRunsAsync(int take = 24, CancellationToken ct = default)
        => _dispatcher.Query(new ListRecentChainRunsQuery(take), ct);

    public Task<DataMappingView> SaveDataMappingAsync(SaveDataMappingCommand command, CancellationToken ct = default)
        => _dispatcher.Send(command, ct);

    public Task<bool> DeleteDataMappingAsync(Guid mappingId, CancellationToken ct = default)
        => _dispatcher.Send(new DeleteDataMappingCommand(mappingId), ct);

    public Task<IReadOnlyList<DataMappingView>> ListDataMappingsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new ListDataMappingsQuery(projectId), ct);

    public Task<Ports.ProjectQueryResult> ExecuteProjectDataAsync(Guid projectId, string sql, CancellationToken ct = default)
        => _dispatcher.Send(new ExecuteProjectDataCommand(projectId, sql), ct);

    public Task<IReadOnlyList<Ports.ProjectTableInfo>> ListProjectDataTablesAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new ListProjectDataTablesQuery(projectId), ct);

    public Task<Ports.ProjectTablePageResult> QueryProjectTablePageAsync(Guid projectId, string tableName, string? search,
        int page, int pageSize, string? sortColumn = null, bool sortDescending = false, CancellationToken ct = default)
        => _dispatcher.Query(new QueryProjectTablePageQuery(projectId, tableName, search, page, pageSize, sortColumn, sortDescending), ct);

    public Task CreateProjectTableAsync(Guid projectId, string tableName, IReadOnlyList<Ports.ProjectColumnSpec> columns, CancellationToken ct = default)
        => _dispatcher.Send(new CreateProjectTableCommand(projectId, tableName, columns), ct);

    public Task<ImportCsvResult> ImportCsvToProjectTableAsync(Guid projectId, string tableName, IReadOnlyList<Ports.ProjectColumnSpec> columns,
        IReadOnlyList<IReadOnlyList<string?>> rows, bool createTable, CancellationToken ct = default)
        => _dispatcher.Send(new ImportCsvToProjectTableCommand(projectId, tableName, columns, rows, createTable), ct);

    public Task RenameProjectTableAsync(Guid projectId, string from, string to, CancellationToken ct = default)
        => _dispatcher.Send(new RenameProjectTableCommand(projectId, from, to), ct);

    public Task DropProjectTableAsync(Guid projectId, string tableName, CancellationToken ct = default)
        => _dispatcher.Send(new DropProjectTableCommand(projectId, tableName), ct);

    public Task<string> ExportProjectTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default)
        => _dispatcher.Query(new ExportProjectTableQuery(projectId, tableName), ct);

    public Task<IReadOnlyList<Ports.ProjectColumnInfo>> ListProjectTableColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default)
        => _dispatcher.Query(new ListProjectTableColumnsQuery(projectId, tableName), ct);

    public Task AddProjectTableColumnAsync(Guid projectId, string tableName, Ports.ProjectColumnSpec column, CancellationToken ct = default)
        => _dispatcher.Send(new AddProjectTableColumnCommand(projectId, tableName, column), ct);

    public Task DropProjectTableColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default)
        => _dispatcher.Send(new DropProjectTableColumnCommand(projectId, tableName, columnName), ct);

    public Task<string> GenerateProjectChartAsync(Guid projectId, string tableName, string? instruction, CancellationToken ct = default)
        => _dispatcher.Send(new GenerateProjectChartCommand(projectId, tableName, instruction), ct);

    public Task<IReadOnlyList<ProjectChartView>> ListProjectChartsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new ListProjectChartsQuery(projectId), ct);

    public Task<ProjectChartView> SaveSqlChartAsync(Guid projectId, string name, string sql, string chartType = "bar", CancellationToken ct = default)
        => _dispatcher.Send(new SaveSqlChartCommand(projectId, name, sql, chartType), ct);

    public Task<bool> DeleteSqlChartAsync(Guid projectId, string name, CancellationToken ct = default)
        => _dispatcher.Send(new DeleteSqlChartCommand(projectId, name), ct);

    public Task<bool> SaveProjectViewAsync(Guid projectId, string name, string selectSql, CancellationToken ct = default)
        => _dispatcher.Send(new SaveProjectViewCommand(projectId, name, selectSql), ct);

    public Task<bool> DropProjectViewAsync(Guid projectId, string name, CancellationToken ct = default)
        => _dispatcher.Send(new DropProjectViewCommand(projectId, name), ct);

    public Task<DataEntityView> SaveDataEntityAsync(SaveDataEntityCommand command, CancellationToken ct = default)
        => _dispatcher.Send(command, ct);

    public Task<bool> DeleteDataEntityAsync(Guid entityId, CancellationToken ct = default)
        => _dispatcher.Send(new DeleteDataEntityCommand(entityId), ct);

    public Task<IReadOnlyList<DataEntityView>> ListDataEntitiesAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new ListDataEntitiesQuery(projectId), ct);

    public Task<IReadOnlyList<Guid>> ListTaggedRunsAsync(Guid entityId, string key, CancellationToken ct = default)
        => _dispatcher.Query(new TaggedRunsQuery(entityId, key), ct);

    public Task<IReadOnlyList<Guid>> ListEntityRunsAsync(Guid entityId, CancellationToken ct = default)
        => _dispatcher.Query(new EntityRunsQuery(entityId), ct);

    public Task<IReadOnlyList<EntityTagPair>> ListEntityTagPairsAsync(Guid entityId, CancellationToken ct = default)
        => _dispatcher.Query(new EntityTagPairsQuery(entityId), ct);

    public Task<CreateEntityRecordResult> CreateEntityRecordAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
        => _dispatcher.Send(new CreateEntityRecordCommand(projectId, tableName, values), ct);

    public Task<int> UpdateEntityRecordAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys,
        IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
        => _dispatcher.Send(new UpdateEntityRecordCommand(projectId, tableName, keys, values), ct);

    public Task<int> DeleteEntityRecordAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys, CancellationToken ct = default)
        => _dispatcher.Send(new DeleteEntityRecordCommand(projectId, tableName, keys), ct);

    public Task<RecordLinkService.RescanResult> RescanRecordLinksAsync(Guid projectId, CancellationToken ct = default)
        => _links.RescanProjectAsync(projectId, ct);

    public Task<IReadOnlyList<RecordLinkGroup>> ListRecordLinkGroupsAsync(Guid projectId, CancellationToken ct = default)
        => _linkStore.GroupsAsync(projectId, ct: ct);

    public Task<IReadOnlyList<RecordLink>> RelatedRecordLinksAsync(Guid projectId, string tableName, string rowKey, CancellationToken ct = default)
        => _linkStore.RelatedAsync(projectId, tableName, rowKey, ct: ct);

    public Task<IReadOnlyList<RunReportView>> ListRecentRunReportsAsync(int take = 24, CancellationToken ct = default)
        => _dispatcher.Query(new ListRecentRunReportsQuery(take), ct);

    public Task<JobView?> GetJobAsync(Guid jobId, CancellationToken ct = default)
        => _dispatcher.Query(new GetJobQuery(jobId), ct);

    // ── Triggers ──────────────────────────────────────────────────────────────────────────────────

    public Task<TriggerView> CreateTriggerAsync(CreateTriggerCommand command, CancellationToken ct = default)
        => _dispatcher.Send(command, ct);

    public Task<TriggerView> SetTriggerEnabledAsync(Guid triggerId, bool enabled, CancellationToken ct = default)
        => _dispatcher.Send(new SetTriggerEnabledCommand(triggerId, enabled), ct);

    public Task<bool> DeleteTriggerAsync(Guid triggerId, CancellationToken ct = default)
        => _dispatcher.Send(new DeleteTriggerCommand(triggerId), ct);

    public Task<IReadOnlyList<TriggerView>> ListTriggersAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new ListTriggersQuery(projectId), ct);

    public Task<TriggerView?> GetTriggerAsync(Guid triggerId, CancellationToken ct = default)
        => _dispatcher.Query(new GetTriggerByIdQuery(triggerId), ct);

    // ── Events ────────────────────────────────────────────────────────────────────────────────────

    public Task<EventTypeView> DefineEventTypeAsync(string name, string? description, string? payloadSchema, CancellationToken ct = default)
        => _dispatcher.Send(new DefineEventTypeCommand(name, description, payloadSchema), ct);

    public Task<EventOccurrenceView> EmitEventAsync(string name, Guid? projectId, string? payload, CancellationToken ct = default)
        => _dispatcher.Send(new EmitEventCommand(name, projectId, payload), ct);

    public Task<IReadOnlyList<EventTypeView>> ListEventTypesAsync(CancellationToken ct = default)
        => _dispatcher.Query(new ListEventTypesQuery(), ct);

    public Task<IReadOnlyList<EventOccurrenceView>> ListEventOccurrencesAsync(int take = 50, CancellationToken ct = default)
        => _dispatcher.Query(new ListEventOccurrencesQuery(take), ct);

    public Task<IReadOnlyList<RunOutputMatchView>> SearchRunOutputsAsync(Guid projectId, string query, int take = 10, CancellationToken ct = default)
        => _dispatcher.Query(new SearchRunOutputsQuery(projectId, query, take), ct);

    public Task<JobView> UploadJobCodeAsync(UploadJobCodeCommand command, CancellationToken ct = default)
        => _dispatcher.Send(command, ct);

    // ── Backup/restore ────────────────────────────────────────────────────────────────────────────

    public Task<BackupManifest> ExportManifestAsync(CancellationToken ct = default)
        => _dispatcher.Query(new ExportManifestQuery(), ct);

    public Task<ImportResultView> ImportManifestAsync(BackupManifest manifest, ImportMode mode = ImportMode.Merge, CancellationToken ct = default)
        => _dispatcher.Send(new ImportManifestCommand(manifest, mode), ct);

    // ── Granular RBAC ─────────────────────────────────────────────────────────────────────────────

    public Task<UserPermissionsView> GetUserPermissionsAsync(Guid userId, CancellationToken ct = default)
        => _dispatcher.Query(new GetUserPermissionsQuery(userId), ct);

    public Task<UserPermissionsView> SetUserPermissionOverrideAsync(Guid userId, string permission, bool? allowed, CancellationToken ct = default)
        => _dispatcher.Send(new SetUserPermissionOverrideCommand(userId, permission, allowed), ct);

    // ── Cluster page: node inventory + promote master + join codes ───────────────────────────────────

    public Task<Ports.ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default)
        => _dispatcher.Query(new PlaceContext.Application.Cluster.GetClusterInfoQuery(), ct);

    public Task<Ports.PromoteMasterResult> PromoteNodeToMasterAsync(string nodeName, CancellationToken ct = default)
        => _dispatcher.Send(new PlaceContext.Application.Cluster.PromoteNodeToMasterCommand(nodeName), ct);

    public Task<Ports.ClusterJoinMaterial?> GetClusterJoinMaterialAsync(CancellationToken ct = default)
        => _dispatcher.Query(new PlaceContext.Application.Cluster.GetClusterJoinMaterialQuery(), ct);

    public Task<Cluster.LaunchAgentResult> LaunchClusterAgentAsync(CancellationToken ct = default)
        => _dispatcher.Send(new PlaceContext.Application.Cluster.LaunchClusterAgentCommand(), ct);

    public Task<Ports.JobTelemetrySnapshot> GetJobTelemetrySnapshotAsync(CancellationToken ct = default)
        => _dispatcher.Query(new Observability.GetJobTelemetrySnapshotQuery(), ct);

    public Task<IReadOnlyList<Ports.JobRunTelemetry>> ListRecentJobRunTelemetryAsync(int take = 50, CancellationToken ct = default)
        => _dispatcher.Query(new Observability.ListRecentJobRunTelemetryQuery(take), ct);

    public Task<IReadOnlyList<Ports.JobRunTelemetry>> ListJobRunTelemetryAsync(Guid jobId, int take = 20, CancellationToken ct = default)
        => _dispatcher.Query(new Observability.ListJobRunTelemetryQuery(jobId, take), ct);

    public Task<IReadOnlyList<Ports.ChainRunTelemetry>> ListRecentChainRunTelemetryAsync(int take = 50, CancellationToken ct = default)
        => _dispatcher.Query(new Observability.ListRecentChainRunTelemetryQuery(take), ct);

    // ── Agent chat ─────────────────────────────────────────────────────────────────────────────────

    public Task<AgentConfigView> GetAgentConfigAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new Features.GetAgentConfigQuery(projectId), ct);

    public Task<AgentConfigView> UpdateAgentConfigAsync(Features.UpdateAgentConfigCommand command, CancellationToken ct = default)
        => _dispatcher.Send(command, ct);

    public Task<AgentChatSessionView> SendAgentMessageAsync(Features.SendAgentMessageCommand command, CancellationToken ct = default)
        => _dispatcher.Send(command, ct);

    public Task<IReadOnlyList<AgentChatSessionView>> ListAgentChatSessionsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new Features.ListAgentChatSessionsQuery(projectId), ct);

    public Task<AgentChatSessionView?> GetAgentChatSessionAsync(Guid sessionId, CancellationToken ct = default)
        => _dispatcher.Query(new Features.GetAgentChatSessionQuery(sessionId), ct);

    public Task<Dtos.McpConnectionView> CreateMcpConnectionAsync(Features.CreateMcpConnectionCommand command, CancellationToken ct = default)
        => _dispatcher.Send(command, ct);
    public Task<Dtos.McpConnectionView> UpdateMcpConnectionAsync(Features.UpdateMcpConnectionCommand command, CancellationToken ct = default)
        => _dispatcher.Send(command, ct);
    public Task<bool> DeleteMcpConnectionAsync(Guid id, CancellationToken ct = default)
        => _dispatcher.Send(new Features.DeleteMcpConnectionCommand(id), ct);
    public Task<Dtos.McpConnectionView> TestMcpConnectionAsync(Guid id, CancellationToken ct = default)
        => _dispatcher.Send(new Features.TestMcpConnectionCommand(id), ct);
    public Task<IReadOnlyList<Dtos.McpConnectionView>> ListMcpConnectionsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new Features.ListMcpConnectionsQuery(projectId), ct);
}
