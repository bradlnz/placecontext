using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Services;

namespace PlaceContext.Projects;

public static class DependencyInjection
{
    public static IServiceCollection AddProjectsModule(this IServiceCollection services)
    {
        services.AddSingleton<ContextStalenessPolicy>();

        services.AddScoped<ICommandHandler<CreateProjectCommand, ProjectSummaryView>, CreateProjectHandler>();
        services.AddScoped<ICommandHandler<RegisterProjectCommand, ProjectSummaryView>, RegisterProjectHandler>();
        services.AddScoped<ICommandHandler<RecordActivityCommand, ActivityRecordView>, RecordActivityHandler>();
        services.AddScoped<ICommandHandler<AddDecisionCommand, DecisionView>, AddDecisionHandler>();
        services.AddScoped<ICommandHandler<SetGlobalRequirementsCommand, RequirementsView>, SetGlobalRequirementsHandler>();
        services.AddScoped<ICommandHandler<SetProjectRequirementsCommand, RequirementsView>, SetProjectRequirementsHandler>();
        services.AddScoped<ICommandHandler<OnboardCommand, OnboardResultView>, OnboardHandler>();

        services.AddScoped<IQueryHandler<GetProjectsQuery, IReadOnlyList<ProjectSummaryView>>, GetProjectsHandler>();
        services.AddScoped<IQueryHandler<GetProjectByIdQuery, ProjectSummaryView?>, GetProjectByIdHandler>();
        services.AddScoped<IQueryHandler<GetProjectOverviewQuery, ProjectOverviewView>, GetProjectOverviewHandler>();
        services.AddScoped<IQueryHandler<GetTimelineQuery, ActivityTimelineView>, GetTimelineHandler>();
        services.AddScoped<IQueryHandler<GetDecisionsQuery, IReadOnlyList<DecisionView>>, GetDecisionsHandler>();
        services.AddScoped<IQueryHandler<SuggestImprovementsQuery, ImprovementsView>, SuggestImprovementsHandler>();
        services.AddScoped<IQueryHandler<GetGlobalRequirementsQuery, RequirementsView>, GetGlobalRequirementsHandler>();
        services.AddScoped<IQueryHandler<GetProjectRequirementsQuery, RequirementsView>, GetProjectRequirementsHandler>();
        services.AddScoped<IQueryHandler<GetEffectiveRequirementsQuery, EffectiveRequirementsView>, GetEffectiveRequirementsHandler>();
        services.AddScoped<IQueryHandler<GetFocusQuery, FocusView>, FocusHandler>();
        services.AddScoped<IQueryHandler<GetRootStatsQuery, RootStatsView>, GetRootStatsHandler>();
        services.AddScoped<IQueryHandler<GetRootActivityQuery, RootActivityView>, GetRootActivityHandler>();
        services.AddScoped<IQueryHandler<GetRecentToolCallsQuery, IReadOnlyList<ToolCallView>>, GetRecentToolCallsHandler>();
        return services;
    }
}
