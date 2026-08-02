using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application;

/// <summary>
/// Thin application facade the Presentation layer (MCP tools + Blazor portal) calls. Each method is
/// a one-liner over the dispatcher — no business logic lives here.
/// </summary>
public interface IPlaceContextService
{
    Task<ProjectSummaryView> CreateProjectAsync(string path, string? name, CancellationToken ct = default);
    Task<OnboardResultView> OnboardAsync(string path, string? name, string agent, int backfillLimit, CancellationToken ct = default);
    Task<ProjectSummaryView> RegisterProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<ProjectSummaryView> RebuildGraphAsync(Guid projectId, bool incremental = true, CancellationToken ct = default);
    Task<ActivityRecordView> RecordActivityAsync(RecordActivityCommand command, CancellationToken ct = default);
    Task<DecisionView> AddDecisionAsync(Guid projectId, string question, string choice, string? rationale, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectSummaryView>> GetProjectsAsync(CancellationToken ct = default);
    Task<ProjectSummaryView?> GetProjectByIdAsync(Guid projectId, CancellationToken ct = default);
    Task<ProjectOverviewView> GetProjectOverviewAsync(Guid projectId, CancellationToken ct = default);
    Task<ActivityTimelineView> GetTimelineAsync(Guid projectId, int take = 50, CancellationToken ct = default);
    Task<IReadOnlyList<DecisionView>> GetDecisionsAsync(Guid projectId, CancellationToken ct = default);
    Task<GraphQueryView> QueryGraphAsync(Guid projectId, string question, CancellationToken ct = default);
    Task<ProjectSecretView> AddProjectSecretAsync(Guid projectId, string name, string value, CancellationToken ct = default);
    Task<bool> DeleteProjectSecretAsync(Guid projectId, string name, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectSecretView>> ListProjectSecretsAsync(Guid projectId, CancellationToken ct = default);
    Task<RequirementsView> GetGlobalRequirementsAsync(CancellationToken ct = default);
    Task<RequirementsView> SetGlobalRequirementsAsync(string markdown, CancellationToken ct = default);
    Task<RequirementsView> GetProjectRequirementsAsync(Guid projectId, CancellationToken ct = default);
    Task<RequirementsView> SetProjectRequirementsAsync(Guid projectId, string markdown, CancellationToken ct = default);
    Task<EffectiveRequirementsView> GetEffectiveRequirementsAsync(Guid projectId, CancellationToken ct = default);
    Task<UsageEntryView> RecordUsageAsync(Guid projectId, string model, long inputTokens, long outputTokens, string? description, CancellationToken ct = default);
    Task<CostDashboardView> GetCostDashboardAsync(Guid projectId, CancellationToken ct = default);
    Task<RootCostView> GetRootCostAsync(CancellationToken ct = default);
    Task<SearchResultsView> SearchAsync(string term, CancellationToken ct = default);
    Task<SearchResultsView> SearchAsync(string term, Guid? projectId, CancellationToken ct = default);
    Task<SearchResultsView> SearchAsync(string term, Guid? projectId, int limit, CancellationToken ct = default);
    Task<FocusView> GetFocusAsync(CancellationToken ct = default);
    Task<ImprovementsView> SuggestImprovementsAsync(Guid projectId, CancellationToken ct = default);
    Task<SkillScaffoldView> ScaffoldSkillAsync(Guid projectId, string skillName, string? description, CancellationToken ct = default);
    Task<SkillScaffoldView> SetupHermesAsync(Guid projectId, CancellationToken ct = default);

    // Job management.
    Task<JobView> CreateJobAsync(CreateJobCommand command, CancellationToken ct = default);
    Task<JobView> UpdateJobAsync(UpdateJobCommand command, CancellationToken ct = default);
    Task<JobRunDetailView> RunJobAsync(Guid jobId, string? inputPayload = null, Guid? runId = null, CancellationToken ct = default);
    Task<JobRunDetailView> ReplayRunAsync(Guid runId, Guid? newRunId = null, CancellationToken ct = default);
    Task<IReadOnlyList<JobView>> ListJobsAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<JobRunView>> ListJobRunsAsync(Guid jobId, CancellationToken ct = default);
    Task<JobRunDetailView?> GetJobRunAsync(Guid runId, CancellationToken ct = default);
    Task<JobTestCaseView> SaveJobTestCaseAsync(
        SaveJobTestCaseCommand command, CancellationToken ct = default);
    Task<bool> DeleteJobTestCaseAsync(Guid testId, CancellationToken ct = default);
    Task<JobTestCaseView> RunJobTestCaseAsync(Guid testId, CancellationToken ct = default);
    Task<IReadOnlyList<JobTestCaseView>> ListJobTestCasesAsync(
        Guid projectId, CancellationToken ct = default);
    Task<JobTestCaseView?> GetJobTestCaseAsync(Guid testId, CancellationToken ct = default);
    Task<JobTestCaseView> UpdateJobTestCodeAsync(
        UpdateJobTestCodeCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<OpenSearchIndexView>> ListOpenSearchIndicesAsync(
        Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<OpenSearchFieldView>> ListOpenSearchFieldsAsync(
        Guid projectId, string indexPattern, CancellationToken ct = default);
    Task<OpenSearchLastUpdatedView> GetOpenSearchLastUpdatedAsync(
        Guid projectId, string indexPattern, IReadOnlyList<string> candidateFields,
        CancellationToken ct = default);
    Task<OpenSearchSearchView> SearchOpenSearchAsync(
        OpenSearchSearchRequest request, CancellationToken ct = default);
    Task<OpenSearchSyncView> TriggerOpenSearchSyncAsync(
        Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<OpenSearchDashboardView>> ListOpenSearchDashboardsAsync(
        Guid projectId, CancellationToken ct = default);
    Task<OpenSearchDashboardView> SaveOpenSearchDashboardAsync(
        SaveOpenSearchDashboardCommand command, CancellationToken ct = default);
    Task<bool> DeleteOpenSearchDashboardAsync(
        Guid dashboardId, CancellationToken ct = default);
    Task<IReadOnlyList<RunArtifactLinkView>> ListRunArtifactsAsync(Guid runId, CancellationToken ct = default);
    Task<IReadOnlyList<RunArtifactLinkView>> ListJobRunArtifactsAsync(Guid jobId, CancellationToken ct = default);
    Task<bool> DeleteJobAsync(Guid jobId, CancellationToken ct = default);
    Task<IReadOnlyList<Features.ArtifactFileView>> ListRecentArtifactsAsync(int take = 100, CancellationToken ct = default);
    Task<IReadOnlyList<Features.ArtifactFileView>> ListProjectArtifactsAsync(Guid projectId, int take = 2000, string? search = null, CancellationToken ct = default);
    Task<bool> DeleteArtifactAsync(Guid artifactId, CancellationToken ct = default);
    Task<int> DeleteArtifactsAsync(IReadOnlyList<Guid> artifactIds, CancellationToken ct = default);
    Task<ArtifactShareCreated> CreateArtifactShareAsync(Guid artifactId, int lifetimeDays = 7, CancellationToken ct = default);
    Task<ArtifactShareStatus?> GetArtifactShareStatusAsync(Guid artifactId, CancellationToken ct = default);
    Task<bool> RevokeArtifactShareAsync(Guid artifactId, CancellationToken ct = default);

    // CRM mode: project-scoped customers and customer-linked job execution.
    Task<CrmClientView> SaveCrmClientAsync(SaveCrmClientCommand command, CancellationToken ct = default);
    Task<CrmClientView> MoveCrmClientAsync(Guid clientId, Domain.ValueObjects.CustomerLifecycleStage stage, CancellationToken ct = default);
    Task<bool> DeleteCrmClientAsync(Guid clientId, CancellationToken ct = default);
    Task<IReadOnlyList<CrmClientView>> ListCrmClientsAsync(Guid projectId, CancellationToken ct = default);
    Task<CrmChainRunView> RunCrmClientAutomationAsync(
        Guid clientId,
        Guid chainId,
        CancellationToken ct = default);
    Task<IReadOnlyList<CrmChainRunView>> ListCrmClientChainRunsAsync(
        Guid clientId,
        int take = 20,
        CancellationToken ct = default);
    Task<CrmCommunicationView> AddCrmClientNoteAsync(Guid clientId, string body, CancellationToken ct = default);
    Task<CrmCommunicationView> SendCrmClientMessageAsync(
        Guid clientId,
        Domain.ValueObjects.CrmCommunicationChannel channel,
        string? subject,
        string body,
        CancellationToken ct = default);
    Task<IReadOnlyList<CrmCommunicationView>> ListCrmClientCommunicationsAsync(
        Guid clientId,
        int take = 100,
        CancellationToken ct = default);
    Task<CrmCommsCapabilitiesView> GetCrmCommsCapabilitiesAsync(CancellationToken ct = default);
    Task<CrmClientArtifactView> AttachCrmClientArtifactAsync(
        Guid clientId,
        string fileName,
        string? contentType,
        byte[] content,
        CancellationToken ct = default);
    Task<IReadOnlyList<CrmClientArtifactView>> ListCrmClientArtifactsAsync(
        Guid clientId,
        int take = 200,
        CancellationToken ct = default);
    Task<bool> RemoveCrmClientArtifactAsync(Guid artifactId, CancellationToken ct = default);
    Task<CrmAutomationRuleView> SaveCrmAutomationRuleAsync(
        SaveCrmAutomationRuleCommand command, CancellationToken ct = default);
    Task<CrmAutomationRuleView> SetCrmAutomationEnabledAsync(
        Guid ruleId, bool enabled, CancellationToken ct = default);
    Task<bool> DeleteCrmAutomationRuleAsync(Guid ruleId, CancellationToken ct = default);
    Task<IReadOnlyList<CrmAutomationRuleView>> ListCrmAutomationRulesAsync(
        Guid projectId, CancellationToken ct = default);

    // Job chains (staged pipelines: each stage's output feeds the next stage's input; a stage with
    // more than one job id is a parallel fan-out group, and the stage right after it is the join).
    // `stages`, when supplied, wins over the backward-compatible flat `stepJobIds` (one job per stage).
    // `stageGates` is an optional parallel list of flow-control gates, one per stage (null = no gate).
    Task<JobChainView> CreateJobChainAsync(Guid projectId, string name, string? description, IReadOnlyList<Guid> stepJobIds, IReadOnlyList<IReadOnlyList<Guid>>? stages = null, IReadOnlyList<Domain.ValueObjects.ChainGate?>? stageGates = null, IReadOnlyList<Domain.ValueObjects.ChainAction?>? stageActions = null, CancellationToken ct = default);
    Task<JobChainView> UpdateJobChainAsync(Guid chainId, string name, string? description, IReadOnlyList<Guid> stepJobIds, IReadOnlyList<IReadOnlyList<Guid>>? stages = null, IReadOnlyList<Domain.ValueObjects.ChainGate?>? stageGates = null, IReadOnlyList<Domain.ValueObjects.ChainAction?>? stageActions = null, CancellationToken ct = default);
    Task<bool> CancelJobRunAsync(Guid runId, CancellationToken ct = default);
    Task<bool> CancelChainRunAsync(Guid chainRunId, CancellationToken ct = default);
    Task<bool> DeleteJobChainAsync(Guid chainId, CancellationToken ct = default);
    Task<IReadOnlyList<JobChainView>> ListJobChainsAsync(Guid projectId, CancellationToken ct = default);
    Task<ChainRunView> RunJobChainAsync(Guid chainId, string? inputPayload = null, Guid? chainRunId = null, IReadOnlyDictionary<int, string>? stepPayloadOverrides = null, CancellationToken ct = default);
    Task<ChainRunView> ReplayJobChainAsync(Features.ReplayJobChainCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<ChainRunView>> ListChainRunsAsync(Guid chainId, int take = 20, CancellationToken ct = default);
    Task<ChainRunView?> GetChainRunAsync(Guid chainRunId, CancellationToken ct = default);
    /// <summary>Cross-project chain run history, newest first — Observability's "Chains" tab.</summary>
    Task<IReadOnlyList<ChainRunReportView>> ListRecentChainRunsAsync(int take = 24, CancellationToken ct = default);

    // Data map (declarative ingestion: job run results → project tables).
    Task<DataMappingView> SaveDataMappingAsync(SaveDataMappingCommand command, CancellationToken ct = default);
    Task<bool> DeleteDataMappingAsync(Guid mappingId, CancellationToken ct = default);
    Task<IReadOnlyList<DataMappingView>> ListDataMappingsAsync(Guid projectId, CancellationToken ct = default);

    // Project data (each project's own database: tables + SQL).
    Task<Ports.ProjectQueryResult> ExecuteProjectDataAsync(Guid projectId, string sql, CancellationToken ct = default);
    Task<IReadOnlyList<Ports.ProjectTableInfo>> ListProjectDataTablesAsync(Guid projectId, CancellationToken ct = default);
    Task<Ports.ProjectTablePageResult> QueryProjectTablePageAsync(Guid projectId, string tableName, string? search,
        int page, int pageSize, string? sortColumn = null, bool sortDescending = false, CancellationToken ct = default);
    Task CreateProjectTableAsync(Guid projectId, string tableName, IReadOnlyList<Ports.ProjectColumnSpec> columns, CancellationToken ct = default);
    Task<ImportCsvResult> ImportCsvToProjectTableAsync(Guid projectId, string tableName, IReadOnlyList<Ports.ProjectColumnSpec> columns,
        IReadOnlyList<IReadOnlyList<string?>> rows, bool createTable, CancellationToken ct = default);
    Task RenameProjectTableAsync(Guid projectId, string from, string to, CancellationToken ct = default);
    Task DropProjectTableAsync(Guid projectId, string tableName, CancellationToken ct = default);
    Task<string> ExportProjectTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default);
    Task<IReadOnlyList<Ports.ProjectColumnInfo>> ListProjectTableColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default);
    Task AddProjectTableColumnAsync(Guid projectId, string tableName, Ports.ProjectColumnSpec column, CancellationToken ct = default);
    Task DropProjectTableColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default);
    Task<string> GenerateProjectChartAsync(Guid projectId, string tableName, string? instruction, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectChartView>> ListProjectChartsAsync(Guid projectId, CancellationToken ct = default);
    Task<ProjectChartView> SaveSqlChartAsync(Guid projectId, string name, string sql, string chartType = "bar", CancellationToken ct = default);
    Task<bool> DeleteSqlChartAsync(Guid projectId, string name, CancellationToken ct = default);
    Task<bool> SaveProjectViewAsync(Guid projectId, string name, string selectSql, CancellationToken ct = default);
    Task<bool> DropProjectViewAsync(Guid projectId, string name, CancellationToken ct = default);
    Task<Features.DataEntityView> SaveDataEntityAsync(Features.SaveDataEntityCommand command, CancellationToken ct = default);
    Task<bool> DeleteDataEntityAsync(Guid entityId, CancellationToken ct = default);
    Task<IReadOnlyList<Features.DataEntityView>> ListDataEntitiesAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ListTaggedRunsAsync(Guid entityId, string key, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ListEntityRunsAsync(Guid entityId, CancellationToken ct = default);
    Task<IReadOnlyList<Features.EntityTagPair>> ListEntityTagPairsAsync(Guid entityId, CancellationToken ct = default);
    Task<CreateEntityRecordResult> CreateEntityRecordAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default);
    Task<int> UpdateEntityRecordAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys,
        IReadOnlyDictionary<string, string?> values, CancellationToken ct = default);
    Task<int> DeleteEntityRecordAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys, CancellationToken ct = default);

    // Record-link index: value-based linking across project tables + duplicate warnings on row add.
    Task<RecordLinkService.RescanResult> RescanRecordLinksAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<RecordLinkGroup>> ListRecordLinkGroupsAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<RecordLink>> RelatedRecordLinksAsync(Guid projectId, string tableName, string rowKey, CancellationToken ct = default);

    Task<IReadOnlyList<RunReportView>> ListRecentRunReportsAsync(int take = 24, CancellationToken ct = default);
    Task<JobView?> GetJobAsync(Guid jobId, CancellationToken ct = default);
    Task<JobView> UploadJobCodeAsync(UploadJobCodeCommand command, CancellationToken ct = default);

    // Triggers (schedule + event) and events.
    Task<TriggerView> CreateTriggerAsync(CreateTriggerCommand command, CancellationToken ct = default);
    Task<TriggerView> UpdateTriggerAsync(UpdateTriggerCommand command, CancellationToken ct = default);
    Task<TriggerView> SetTriggerEnabledAsync(Guid triggerId, bool enabled, CancellationToken ct = default);
    Task<bool> DeleteTriggerAsync(Guid triggerId, CancellationToken ct = default);
    Task<IReadOnlyList<TriggerView>> ListTriggersAsync(Guid projectId, CancellationToken ct = default);
    Task<TriggerView?> GetTriggerAsync(Guid triggerId, CancellationToken ct = default);
    Task<EventTypeView> DefineEventTypeAsync(string name, string? description, string? payloadSchema, CancellationToken ct = default);
    Task<EventOccurrenceView> EmitEventAsync(string name, Guid? projectId, string? payload, CancellationToken ct = default);
    Task<IReadOnlyList<EventTypeView>> ListEventTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EventOccurrenceView>> ListEventOccurrencesAsync(int take = 50, CancellationToken ct = default);
    Task<IReadOnlyList<RunOutputMatchView>> SearchRunOutputsAsync(Guid projectId, string query, int take = 10, CancellationToken ct = default);

    // Root-level read models for the redesigned portal.
    Task<RootStatsView> GetRootStatsAsync(CancellationToken ct = default);
    Task<RootActivityView> GetRootActivityAsync(int take = 40, CancellationToken ct = default);
    Task<GraphVizView> GetGraphVizAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<ToolCallView>> GetRecentToolCallsAsync(int take = 100, CancellationToken ct = default);

    // Backup/restore (tenant settings + job definitions → a portable manifest).
    Task<BackupManifest> ExportManifestAsync(CancellationToken ct = default);
    Task<ImportResultView> ImportManifestAsync(BackupManifest manifest, ImportMode mode = ImportMode.Merge, CancellationToken ct = default);

    // Granular RBAC: a member's permission matrix (role defaults + overrides) and editing an override.
    Task<UserPermissionsView> GetUserPermissionsAsync(Guid userId, CancellationToken ct = default);
    Task<UserPermissionsView> SetUserPermissionOverrideAsync(Guid userId, string permission, bool? allowed, CancellationToken ct = default);

    // Editable roles: list definitions (with member counts), create custom roles, edit any non-Owner
    // role's grant set, and delete custom roles no member holds.
    Task<IReadOnlyList<RoleView>> ListRolesAsync(CancellationToken ct = default);
    Task<RoleView> CreateRoleAsync(string name, IReadOnlyList<string> permissions, CancellationToken ct = default);
    Task<RoleView> UpdateRolePermissionsAsync(Guid roleId, IReadOnlyList<string> permissions, CancellationToken ct = default);
    Task<bool> DeleteRoleAsync(Guid roleId, CancellationToken ct = default);

    // Cluster page: node inventory, promote-to-master, join codes (Tailscale fleet).
    Task<Ports.ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default);
    Task<Ports.PromoteMasterResult> PromoteNodeToMasterAsync(string nodeName, CancellationToken ct = default);
    Task<Ports.ClusterJoinMaterial?> GetClusterJoinMaterialAsync(CancellationToken ct = default);
    Task<Cluster.LaunchAgentResult> LaunchClusterAgentAsync(CancellationToken ct = default);
    Task<string> CreateAgentJoinTokenAsync(CancellationToken ct = default);
    Task<Ports.JobTelemetrySnapshot> GetJobTelemetrySnapshotAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Ports.JobRunTelemetry>> ListRecentJobRunTelemetryAsync(int take = 50, CancellationToken ct = default);
    Task<IReadOnlyList<Ports.JobRunTelemetry>> ListJobRunTelemetryAsync(Guid jobId, int take = 20, CancellationToken ct = default);
    Task<IReadOnlyList<Ports.ChainRunTelemetry>> ListRecentChainRunTelemetryAsync(int take = 50, CancellationToken ct = default);

    // Agent chat (Phase 1).
    Task<AgentConfigView> GetAgentConfigAsync(Guid projectId, CancellationToken ct = default);
    Task<AgentConfigView> UpdateAgentConfigAsync(UpdateAgentConfigCommand command, CancellationToken ct = default);
    Task<AgentChatSessionView> SendAgentMessageAsync(SendAgentMessageCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<AgentChatSessionView>> ListAgentChatSessionsAsync(Guid projectId, CancellationToken ct = default);
    Task<AgentChatSessionView?> GetAgentChatSessionAsync(Guid sessionId, CancellationToken ct = default);

    // Chat commands
    Task<ChatCommandView> CreateChatCommandAsync(Features.CreateChatCommandCommand command, CancellationToken ct = default);
    Task<ChatCommandView> UpdateChatCommandAsync(Features.UpdateChatCommandCommand command, CancellationToken ct = default);
    Task<bool> DeleteChatCommandAsync(Guid commandId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatCommandView>> ListChatCommandsAsync(Guid projectId, CancellationToken ct = default);

    // MCP connections
    Task<McpConnectionView> CreateMcpConnectionAsync(Features.CreateMcpConnectionCommand command, CancellationToken ct = default);
    Task<McpConnectionView> UpdateMcpConnectionAsync(Features.UpdateMcpConnectionCommand command, CancellationToken ct = default);
    Task<bool> DeleteMcpConnectionAsync(Guid id, CancellationToken ct = default);
    Task<McpConnectionView> TestMcpConnectionAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<McpConnectionView>> ListMcpConnectionsAsync(Guid projectId, CancellationToken ct = default);
}
