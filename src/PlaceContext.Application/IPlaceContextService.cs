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
    Task<ChangeRecordView> RecordChangeAsync(RecordChangeCommand command, CancellationToken ct = default);
    Task<DebtDashboardView> RecomputeDebtAsync(Guid projectId, CancellationToken ct = default);
    Task<DecisionView> AddDecisionAsync(Guid projectId, string question, string choice, string? rationale, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectSummaryView>> GetProjectsAsync(CancellationToken ct = default);
    Task<ProjectOverviewView> GetProjectOverviewAsync(Guid projectId, CancellationToken ct = default);
    Task<ChangeTimelineView> GetTimelineAsync(Guid projectId, int take = 50, CancellationToken ct = default);
    Task<DebtDashboardView> GetDebtDashboardAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<DecisionView>> GetDecisionsAsync(Guid projectId, CancellationToken ct = default);
    Task<GraphQueryView> QueryGraphAsync(Guid projectId, string question, CancellationToken ct = default);
    Task<ProjectContextView> AddContextAsync(Guid projectId, string section, CancellationToken ct = default);
    Task<ProjectContextView> SetContextAsync(Guid projectId, string markdown, CancellationToken ct = default);
    Task<ProjectContextView> GetContextAsync(Guid projectId, CancellationToken ct = default);
    Task<CodeRequirementsView> GetGlobalRequirementsAsync(CancellationToken ct = default);
    Task<CodeRequirementsView> SetGlobalRequirementsAsync(string markdown, CancellationToken ct = default);
    Task<CodeRequirementsView> GetProjectRequirementsAsync(Guid projectId, CancellationToken ct = default);
    Task<CodeRequirementsView> SetProjectRequirementsAsync(Guid projectId, string markdown, CancellationToken ct = default);
    Task<EffectiveRequirementsView> GetEffectiveRequirementsAsync(Guid projectId, CancellationToken ct = default);
    Task<UsageEntryView> RecordUsageAsync(Guid projectId, string model, long inputTokens, long outputTokens, string? description, CancellationToken ct = default);
    Task<CostDashboardView> GetCostDashboardAsync(Guid projectId, CancellationToken ct = default);
    Task<RootCostView> GetRootCostAsync(CancellationToken ct = default);
    Task<SearchResultsView> SearchAsync(string term, CancellationToken ct = default);
    Task<ImprovementsView> SuggestImprovementsAsync(Guid projectId, CancellationToken ct = default);
    Task<SkillScaffoldView> ScaffoldSkillAsync(Guid projectId, string skillName, string? description, CancellationToken ct = default);

    // Root-level read models for the redesigned portal.
    Task<RootStatsView> GetRootStatsAsync(CancellationToken ct = default);
    Task<RootDebtView> GetRootDebtAsync(CancellationToken ct = default);
    Task<RootLedgerView> GetRootLedgerAsync(int take = 40, CancellationToken ct = default);
    Task<GraphVizView> GetGraphVizAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<ToolCallView>> GetRecentToolCallsAsync(int take = 100, CancellationToken ct = default);
}
