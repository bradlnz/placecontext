using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Services;

namespace PlaceContext.Search;

public static class DependencyInjection
{
    public static IServiceCollection AddSearchApi(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<SearchQuery, SearchResultsView>, SearchHandler>();
        services.AddScoped<IQueryHandler<SearchRunOutputsQuery, IReadOnlyList<RunOutputMatchView>>, SearchRunOutputsHandler>();
        services.AddScoped<IQueryHandler<ListOpenSearchIndicesQuery, IReadOnlyList<OpenSearchIndexView>>, ListOpenSearchIndicesHandler>();
        services.AddScoped<IQueryHandler<ListOpenSearchFieldsQuery, IReadOnlyList<OpenSearchFieldView>>, ListOpenSearchFieldsHandler>();
        services.AddScoped<IQueryHandler<SearchOpenSearchQuery, OpenSearchSearchView>, SearchOpenSearchHandler>();
        return services;
    }

    public static IServiceCollection AddSearchModule(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<SaveOpenSearchDashboardCommand, OpenSearchDashboardView>, SaveOpenSearchDashboardHandler>();
        services.AddScoped<ICommandHandler<DeleteOpenSearchDashboardCommand, bool>, DeleteOpenSearchDashboardHandler>();
        services.AddScoped<ICommandHandler<TriggerOpenSearchSyncCommand, OpenSearchSyncView>, TriggerOpenSearchSyncHandler>();
        services.AddScoped<IQueryHandler<SearchQuery, SearchResultsView>, SearchHandler>();
        services.AddScoped<IQueryHandler<SearchRunOutputsQuery, IReadOnlyList<RunOutputMatchView>>, SearchRunOutputsHandler>();
        services.AddScoped<IQueryHandler<ListOpenSearchIndicesQuery, IReadOnlyList<OpenSearchIndexView>>, ListOpenSearchIndicesHandler>();
        services.AddScoped<IQueryHandler<ListOpenSearchFieldsQuery, IReadOnlyList<OpenSearchFieldView>>, ListOpenSearchFieldsHandler>();
        services.AddScoped<IQueryHandler<GetOpenSearchLastUpdatedQuery, OpenSearchLastUpdatedView>, GetOpenSearchLastUpdatedHandler>();
        services.AddScoped<IQueryHandler<SearchOpenSearchQuery, OpenSearchSearchView>, SearchOpenSearchHandler>();
        services.AddScoped<IQueryHandler<SearchOpenSearchSqlQuery, OpenSearchSqlResult>, SearchOpenSearchSqlHandler>();
        services.AddScoped<IQueryHandler<ListOpenSearchDashboardsQuery, IReadOnlyList<OpenSearchDashboardView>>, ListOpenSearchDashboardsHandler>();
        return services;
    }
}
