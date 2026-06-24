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

    public Task<ChangeRecordView> RecordChangeAsync(RecordChangeCommand command, CancellationToken ct = default)
        => _dispatcher.Send(command, ct);

    public Task<DebtDashboardView> RecomputeDebtAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Send(new RecomputeDebtCommand(projectId), ct);

    public Task<DecisionView> AddDecisionAsync(Guid projectId, string question, string choice, string? rationale, CancellationToken ct = default)
        => _dispatcher.Send(new AddDecisionCommand(projectId, question, choice, rationale), ct);

    public Task<IReadOnlyList<ProjectSummaryView>> GetProjectsAsync(CancellationToken ct = default)
        => _dispatcher.Query(new GetProjectsQuery(), ct);

    public Task<ProjectOverviewView> GetProjectOverviewAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetProjectOverviewQuery(projectId), ct);

    public Task<ChangeTimelineView> GetTimelineAsync(Guid projectId, int take = 50, CancellationToken ct = default)
        => _dispatcher.Query(new GetTimelineQuery(projectId, take), ct);

    public Task<DebtDashboardView> GetDebtDashboardAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetDebtDashboardQuery(projectId), ct);

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

    public Task<CodeRequirementsView> GetGlobalRequirementsAsync(CancellationToken ct = default)
        => _dispatcher.Query(new GetGlobalRequirementsQuery(), ct);

    public Task<CodeRequirementsView> SetGlobalRequirementsAsync(string markdown, CancellationToken ct = default)
        => _dispatcher.Send(new SetGlobalRequirementsCommand(markdown), ct);

    public Task<CodeRequirementsView> GetProjectRequirementsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetProjectRequirementsQuery(projectId), ct);

    public Task<CodeRequirementsView> SetProjectRequirementsAsync(Guid projectId, string markdown, CancellationToken ct = default)
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

    public Task<WorkItemView> AddWorkItemAsync(Guid projectId, string title, string? detail, string priority, CancellationToken ct = default)
        => _dispatcher.Send(new AddWorkItemCommand(projectId, title, detail, priority), ct);

    public Task<WorkItemView?> NextWorkItemAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Send(new NextWorkItemCommand(projectId), ct);

    public Task<WorkItemView> CompleteWorkItemAsync(Guid workItemId, CancellationToken ct = default)
        => _dispatcher.Send(new CompleteWorkItemCommand(workItemId), ct);

    public Task<IReadOnlyList<WorkItemView>> GetWorkItemsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetWorkItemsQuery(projectId), ct);

    public Task<ImprovementsView> SuggestImprovementsAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new SuggestImprovementsQuery(projectId), ct);

    public Task<SkillScaffoldView> ScaffoldSkillAsync(Guid projectId, string skillName, string? description, CancellationToken ct = default)
        => _dispatcher.Send(new ScaffoldSkillCommand(projectId, skillName, description), ct);

    public Task<RootStatsView> GetRootStatsAsync(CancellationToken ct = default)
        => _dispatcher.Query(new GetRootStatsQuery(), ct);

    public Task<RootDebtView> GetRootDebtAsync(CancellationToken ct = default)
        => _dispatcher.Query(new GetRootDebtQuery(), ct);

    public Task<RootLedgerView> GetRootLedgerAsync(int take = 40, CancellationToken ct = default)
        => _dispatcher.Query(new GetRootLedgerQuery(take), ct);

    public Task<GraphVizView> GetGraphVizAsync(Guid projectId, CancellationToken ct = default)
        => _dispatcher.Query(new GetGraphVizQuery(projectId), ct);

    public Task<IReadOnlyList<ToolCallView>> GetRecentToolCallsAsync(int take = 100, CancellationToken ct = default)
        => _dispatcher.Query(new GetRecentToolCallsQuery(take), ct);
}
