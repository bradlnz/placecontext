using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;

namespace PlaceContext.Application;

public sealed class PlaceContextService : IPlaceContextService
{
    private readonly IDispatcher _dispatcher;
    public PlaceContextService(IDispatcher dispatcher) => _dispatcher = dispatcher;

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

    public Task<RiskDashboardView> RecomputeRiskAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Send(new RecomputeRiskCommand(projectId), ct);

    public Task<DecisionView> AddDecisionAsync(Guid projectId, string question, string choice, string? rationale, CancellationToken ct = default)
        => _dispatcher.Send(new AddDecisionCommand(projectId, question, choice, rationale), ct);

    public Task<IReadOnlyList<ProjectSummaryView>> GetProjectsAsync(CancellationToken ct = default)
        => _dispatcher.Query(new GetProjectsQuery(), ct);

    public Task<ProjectOverviewView> GetProjectOverviewAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetProjectOverviewQuery(projectId), ct);

    public Task<ActivityTimelineView> GetTimelineAsync(Guid projectId, int take = 50, CancellationToken ct = default)
        => _dispatcher.Query(new GetTimelineQuery(projectId, take), ct);

    public Task<RiskDashboardView> GetRiskDashboardAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetRiskDashboardQuery(projectId), ct);

    public Task<IReadOnlyList<DecisionView>> GetDecisionsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetDecisionsQuery(projectId), ct);

    public Task<GraphQueryView> QueryGraphAsync(Guid projectId, string question, CancellationToken ct = default)
        => _dispatcher.Query(new QueryGraphQuery(projectId, question), ct);

    public Task<ProjectContextView> AddContextAsync(Guid projectId, string section, CancellationToken ct = default)
        => _dispatcher.Send(new AddContextCommand(projectId, section), ct);

    public Task<ProjectContextView> SetContextAsync(Guid projectId, string markdown, CancellationToken ct = default)
        => _dispatcher.Send(new SetContextCommand(projectId, markdown), ct);

    public Task<ProjectContextView> GetContextAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetContextQuery(projectId), ct);

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

    public Task<GraphVizView> GetBrainAsync(CancellationToken ct = default)
        => _dispatcher.Query(new GetBrainQuery(), ct);

    public Task<WorkItemView> AddWorkItemAsync(Guid projectId, string title, string? detail, string priority, CancellationToken ct = default)
        => _dispatcher.Send(new AddWorkItemCommand(projectId, title, detail, priority), ct);

    public Task<WorkItemView?> NextWorkItemAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Send(new NextWorkItemCommand(projectId), ct);

    public Task<WorkItemView> CompleteWorkItemAsync(Guid workItemId, CancellationToken ct = default)
        => _dispatcher.Send(new CompleteWorkItemCommand(workItemId), ct);

    public Task<WorkItemView> MoveWorkItemAsync(Guid workItemId, string status, CancellationToken ct = default)
        => _dispatcher.Send(new MoveWorkItemCommand(workItemId, status), ct);

    public Task<IReadOnlyList<WorkItemView>> GetWorkItemsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetWorkItemsQuery(projectId), ct);

    public Task<ImprovementsView> SuggestImprovementsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new SuggestImprovementsQuery(projectId), ct);

    public Task<ReportView> GenerateReportAsync(Guid projectId, string? templateName, bool createWorkItems, CancellationToken ct = default)
        => _dispatcher.Send(new GenerateReportCommand(projectId, templateName, createWorkItems), ct);

    public Task<ReportView> SynthesizeContextAsync(Guid projectId, bool createWorkItems, CancellationToken ct = default)
        => _dispatcher.Send(new GenerateReportCommand(projectId, Domain.Services.BuiltInReportTemplates.OnboardingBriefName, createWorkItems), ct);

    public Task<IReadOnlyList<ReportTemplateView>> ListReportTemplatesAsync(CancellationToken ct = default)
        => _dispatcher.Query(new ListReportTemplatesQuery(), ct);

    public Task<ReportTemplateView> DefineReportTemplateAsync(string name, string description, IReadOnlyList<string> sources, CancellationToken ct = default)
        => _dispatcher.Send(new DefineReportTemplateCommand(name, description, sources), ct);

    public Task<SkillScaffoldView> ScaffoldSkillAsync(Guid projectId, string skillName, string? description, CancellationToken ct = default)
        => _dispatcher.Send(new ScaffoldSkillCommand(projectId, skillName, description), ct);

    public Task<RootStatsView> GetRootStatsAsync(CancellationToken ct = default)
        => _dispatcher.Query(new GetRootStatsQuery(), ct);

    public Task<RootRiskView> GetRootRiskAsync(CancellationToken ct = default)
        => _dispatcher.Query(new GetRootRiskQuery(), ct);

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

    public Task<JobRunDetailView> RunJobAsync(Guid jobId, string? inputPayload = null, CancellationToken ct = default)
        => _dispatcher.Send(new RunJobCommand(jobId, inputPayload), ct);

    public Task<IReadOnlyList<JobView>> ListJobsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new ListJobsQuery(projectId), ct);

    public Task<IReadOnlyList<JobRunView>> ListJobRunsAsync(Guid jobId, CancellationToken ct = default)
        => _dispatcher.Query(new ListJobRunsQuery(jobId), ct);

    public Task<JobRunDetailView?> GetJobRunAsync(Guid runId, CancellationToken ct = default)
        => _dispatcher.Query(new GetJobRunQuery(runId), ct);

    public Task<IReadOnlyList<RunArtifactLinkView>> ListRunArtifactsAsync(Guid runId, CancellationToken ct = default)
        => _dispatcher.Query(new ListRunArtifactsQuery(runId), ct);

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
}
