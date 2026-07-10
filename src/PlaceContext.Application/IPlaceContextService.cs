using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;

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
    Task<RiskDashboardView> RecomputeRiskAsync(Guid projectId, CancellationToken ct = default);
    Task<DecisionView> AddDecisionAsync(Guid projectId, string question, string choice, string? rationale, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectSummaryView>> GetProjectsAsync(CancellationToken ct = default);
    Task<ProjectOverviewView> GetProjectOverviewAsync(Guid projectId, CancellationToken ct = default);
    Task<ActivityTimelineView> GetTimelineAsync(Guid projectId, int take = 50, CancellationToken ct = default);
    Task<RiskDashboardView> GetRiskDashboardAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<DecisionView>> GetDecisionsAsync(Guid projectId, CancellationToken ct = default);
    Task<GraphQueryView> QueryGraphAsync(Guid projectId, string question, CancellationToken ct = default);
    Task<ProjectContextView> AddContextAsync(Guid projectId, string section, CancellationToken ct = default);
    Task<ProjectContextView> SetContextAsync(Guid projectId, string markdown, CancellationToken ct = default);
    Task<ProjectContextView> GetContextAsync(Guid projectId, CancellationToken ct = default);
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
    Task<FocusView> GetFocusAsync(CancellationToken ct = default);
    Task<GraphVizView> GetBrainAsync(CancellationToken ct = default);
    Task<WorkItemView> AddWorkItemAsync(Guid projectId, string title, string? detail, string priority, CancellationToken ct = default);
    Task<WorkItemView?> NextWorkItemAsync(Guid projectId, CancellationToken ct = default);
    Task<WorkItemView> CompleteWorkItemAsync(Guid workItemId, CancellationToken ct = default);
    Task<WorkItemView> MoveWorkItemAsync(Guid workItemId, string status, CancellationToken ct = default);
    Task<IReadOnlyList<WorkItemView>> GetWorkItemsAsync(Guid projectId, CancellationToken ct = default);
    Task<ImprovementsView> SuggestImprovementsAsync(Guid projectId, CancellationToken ct = default);
    Task<SkillScaffoldView> ScaffoldSkillAsync(Guid projectId, string skillName, string? description, CancellationToken ct = default);
    Task<SkillScaffoldView> SetupHermesAsync(Guid projectId, CancellationToken ct = default);

    // Report generation layer.
    Task<ReportView> GenerateReportAsync(Guid projectId, string? templateName, bool createWorkItems, CancellationToken ct = default);
    Task<ReportView> SynthesizeContextAsync(Guid projectId, bool createWorkItems, CancellationToken ct = default);
    Task<IReadOnlyList<ReportTemplateView>> ListReportTemplatesAsync(CancellationToken ct = default);
    Task<ReportTemplateView> DefineReportTemplateAsync(string name, string description, IReadOnlyList<string> sources, CancellationToken ct = default);

    // Job management.
    Task<JobView> CreateJobAsync(CreateJobCommand command, CancellationToken ct = default);
    Task<JobView> UpdateJobAsync(UpdateJobCommand command, CancellationToken ct = default);
    Task<JobRunDetailView> RunJobAsync(Guid jobId, string? inputPayload = null, CancellationToken ct = default);
    Task<IReadOnlyList<JobView>> ListJobsAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<JobRunView>> ListJobRunsAsync(Guid jobId, CancellationToken ct = default);
    Task<JobRunDetailView?> GetJobRunAsync(Guid runId, CancellationToken ct = default);
    Task<IReadOnlyList<RunArtifactLinkView>> ListRunArtifactsAsync(Guid runId, CancellationToken ct = default);
    Task<IReadOnlyList<RunArtifactLinkView>> ListJobRunArtifactsAsync(Guid jobId, CancellationToken ct = default);

    // Job chains (ordered pipelines: each step's output feeds the next step's input).
    Task<JobChainView> CreateJobChainAsync(Guid projectId, string name, string? description, IReadOnlyList<Guid> stepJobIds, CancellationToken ct = default);
    Task<JobChainView> UpdateJobChainAsync(Guid chainId, string name, string? description, IReadOnlyList<Guid> stepJobIds, CancellationToken ct = default);
    Task<bool> DeleteJobChainAsync(Guid chainId, CancellationToken ct = default);
    Task<IReadOnlyList<JobChainView>> ListJobChainsAsync(Guid projectId, CancellationToken ct = default);
    Task<ChainRunView> RunJobChainAsync(Guid chainId, string? inputPayload = null, CancellationToken ct = default);

    // Project data (each project's own database: tables + SQL).
    Task<Ports.ProjectQueryResult> ExecuteProjectDataAsync(Guid projectId, string sql, CancellationToken ct = default);
    Task<IReadOnlyList<Ports.ProjectTableInfo>> ListProjectDataTablesAsync(Guid projectId, CancellationToken ct = default);
    Task CreateProjectTableAsync(Guid projectId, string tableName, IReadOnlyList<Ports.ProjectColumnSpec> columns, CancellationToken ct = default);
    Task RenameProjectTableAsync(Guid projectId, string from, string to, CancellationToken ct = default);
    Task DropProjectTableAsync(Guid projectId, string tableName, CancellationToken ct = default);
    Task<string> ExportProjectTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default);
    Task<IReadOnlyList<Ports.ProjectColumnInfo>> ListProjectTableColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default);
    Task AddProjectTableColumnAsync(Guid projectId, string tableName, Ports.ProjectColumnSpec column, CancellationToken ct = default);
    Task DropProjectTableColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default);
    Task<string> GenerateProjectChartAsync(Guid projectId, string tableName, string? instruction, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectChartView>> ListProjectChartsAsync(Guid projectId, CancellationToken ct = default);

    // Inbound SMS gateway (encrypted at rest).
    Task<InboundSmsView> ReceiveInboundSmsAsync(ReceiveInboundSmsCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<InboundSmsView>> ListInboundSmsAsync(int take = 50, CancellationToken ct = default);

    // Application runtime (per-project containers in the DinD daemon).
    Task<IReadOnlyList<Ports.ContainerInfo>> ListProjectContainersAsync(Guid projectId, CancellationToken ct = default);
    Task<string> GetContainerLogsAsync(Guid projectId, string containerId, int tail = 200, CancellationToken ct = default);
    Task RestartProjectContainerAsync(Guid projectId, string containerId, CancellationToken ct = default);
    Task StopProjectContainerAsync(Guid projectId, string containerId, CancellationToken ct = default);
    Task<IReadOnlyList<RunReportView>> ListRecentRunReportsAsync(int take = 24, CancellationToken ct = default);
    Task<JobView?> GetJobAsync(Guid jobId, CancellationToken ct = default);
    Task<JobView> UploadJobCodeAsync(UploadJobCodeCommand command, CancellationToken ct = default);

    // Triggers (schedule + event) and events.
    Task<TriggerView> CreateTriggerAsync(CreateTriggerCommand command, CancellationToken ct = default);
    Task<TriggerView> SetTriggerEnabledAsync(Guid triggerId, bool enabled, CancellationToken ct = default);
    Task<bool> DeleteTriggerAsync(Guid triggerId, CancellationToken ct = default);
    Task<IReadOnlyList<TriggerView>> ListTriggersAsync(Guid projectId, CancellationToken ct = default);
    Task<EventTypeView> DefineEventTypeAsync(string name, string? description, string? payloadSchema, CancellationToken ct = default);
    Task<EventOccurrenceView> EmitEventAsync(string name, Guid? projectId, string? payload, CancellationToken ct = default);
    Task<IReadOnlyList<EventTypeView>> ListEventTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EventOccurrenceView>> ListEventOccurrencesAsync(int take = 50, CancellationToken ct = default);
    Task<IReadOnlyList<RunOutputMatchView>> SearchRunOutputsAsync(Guid projectId, string query, int take = 10, CancellationToken ct = default);

    // Root-level read models for the redesigned portal.
    Task<RootStatsView> GetRootStatsAsync(CancellationToken ct = default);
    Task<RootRiskView> GetRootRiskAsync(CancellationToken ct = default);
    Task<RootActivityView> GetRootActivityAsync(int take = 40, CancellationToken ct = default);
    Task<GraphVizView> GetGraphVizAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<ToolCallView>> GetRecentToolCallsAsync(int take = 100, CancellationToken ct = default);
}
