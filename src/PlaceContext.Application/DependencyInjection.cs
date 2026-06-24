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
        services.AddSingleton<DebtScoreCalculator>();
        services.AddSingleton<AgenticDebtScorer>();
        services.AddSingleton<TechnicalDebtScorer>();
        services.AddSingleton<ContextStalenessPolicy>();
        services.AddSingleton<DecisionTreeAssembler>();
        services.AddSingleton<TokenCostCalculator>();

        // Decision tree (replaces the graphify reader).
        services.AddScoped<IDecisionTreeProvider, DecisionTreeProvider>();

        // Shared debt computation (used by recompute + project creation).
        services.AddScoped<DebtAssessmentService>();

        // Commands.
        services.AddScoped<ICommandHandler<CreateProjectCommand, ProjectSummaryView>, CreateProjectHandler>();
        services.AddScoped<ICommandHandler<RegisterProjectCommand, ProjectSummaryView>, RegisterProjectHandler>();
        services.AddScoped<ICommandHandler<RebuildGraphCommand, ProjectSummaryView>, RebuildGraphHandler>();
        services.AddScoped<ICommandHandler<RecordChangeCommand, ChangeRecordView>, RecordChangeHandler>();
        services.AddScoped<ICommandHandler<RecomputeDebtCommand, DebtDashboardView>, RecomputeDebtHandler>();
        services.AddScoped<ICommandHandler<AddDecisionCommand, DecisionView>, AddDecisionHandler>();
        services.AddScoped<ICommandHandler<AddContextCommand, ProjectContextView>, AddContextHandler>();
        services.AddScoped<ICommandHandler<SetContextCommand, ProjectContextView>, SetContextHandler>();
        services.AddScoped<ICommandHandler<ScaffoldSkillCommand, SkillScaffoldView>, ScaffoldSkillHandler>();
        services.AddScoped<ICommandHandler<SetGlobalRequirementsCommand, CodeRequirementsView>, SetGlobalRequirementsHandler>();
        services.AddScoped<ICommandHandler<SetProjectRequirementsCommand, CodeRequirementsView>, SetProjectRequirementsHandler>();
        services.AddScoped<ICommandHandler<RecordUsageCommand, UsageEntryView>, RecordUsageHandler>();
        services.AddScoped<ICommandHandler<OnboardCommand, OnboardResultView>, OnboardHandler>();

        // Queries.
        services.AddScoped<IQueryHandler<GetProjectsQuery, IReadOnlyList<ProjectSummaryView>>, GetProjectsHandler>();
        services.AddScoped<IQueryHandler<GetProjectOverviewQuery, ProjectOverviewView>, GetProjectOverviewHandler>();
        services.AddScoped<IQueryHandler<GetTimelineQuery, ChangeTimelineView>, GetTimelineHandler>();
        services.AddScoped<IQueryHandler<GetDebtDashboardQuery, DebtDashboardView>, GetDebtDashboardHandler>();
        services.AddScoped<IQueryHandler<GetDecisionsQuery, IReadOnlyList<DecisionView>>, GetDecisionsHandler>();
        services.AddScoped<IQueryHandler<QueryGraphQuery, GraphQueryView>, QueryGraphHandler>();
        services.AddScoped<IQueryHandler<GetContextQuery, ProjectContextView>, GetContextHandler>();
        services.AddScoped<IQueryHandler<SuggestImprovementsQuery, ImprovementsView>, SuggestImprovementsHandler>();
        services.AddScoped<IQueryHandler<GetGlobalRequirementsQuery, CodeRequirementsView>, GetGlobalRequirementsHandler>();
        services.AddScoped<IQueryHandler<GetProjectRequirementsQuery, CodeRequirementsView>, GetProjectRequirementsHandler>();
        services.AddScoped<IQueryHandler<GetEffectiveRequirementsQuery, EffectiveRequirementsView>, GetEffectiveRequirementsHandler>();
        services.AddScoped<IQueryHandler<GetCostDashboardQuery, CostDashboardView>, GetCostDashboardHandler>();
        services.AddScoped<IQueryHandler<GetRootCostQuery, RootCostView>, GetRootCostHandler>();
        services.AddScoped<IQueryHandler<SearchQuery, SearchResultsView>, SearchHandler>();

        // Root-level read models (redesigned portal).
        services.AddScoped<IQueryHandler<GetRootStatsQuery, RootStatsView>, GetRootStatsHandler>();
        services.AddScoped<IQueryHandler<GetRootDebtQuery, RootDebtView>, GetRootDebtHandler>();
        services.AddScoped<IQueryHandler<GetRootLedgerQuery, RootLedgerView>, GetRootLedgerHandler>();
        services.AddScoped<IQueryHandler<GetGraphVizQuery, GraphVizView>, GetGraphVizHandler>();
        services.AddScoped<IQueryHandler<GetRecentToolCallsQuery, IReadOnlyList<ToolCallView>>, GetRecentToolCallsHandler>();

        return services;
    }
}
