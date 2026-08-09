using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Analytics;
using PlaceContext.Data.Contracts.Graph;
using PlaceContext.Domain.Services;

namespace PlaceContext.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddDataApi(this IServiceCollection services)
    {
        services.TryAddSingleton<AnalyticsRefreshQueue>();
        services.AddScoped<DataMappingIngestionService>();
        services.AddScoped<EntityTagService>();
        services.AddScoped<RecordLinkService>();
        services.AddScoped<IQueryHandler<ListDataEntitiesQuery, IReadOnlyList<DataEntityView>>, ListDataEntitiesHandler>();
        services.AddScoped<IQueryHandler<ListDataMappingsQuery, IReadOnlyList<DataMappingView>>, ListDataMappingsHandler>();
        services.AddScoped<IQueryHandler<ListProjectDataTablesQuery, IReadOnlyList<ProjectTableInfo>>, ListProjectDataTablesHandler>();
        services.AddScoped<IQueryHandler<QueryProjectTablePageQuery, ProjectTablePageResult>, QueryProjectTablePageHandler>();
        services.AddScoped<IQueryHandler<ListProjectTableColumnsQuery, IReadOnlyList<ProjectColumnInfo>>, ListProjectTableColumnsHandler>();
        services.AddScoped<ICommandHandler<CreateEntityRecordCommand, CreateEntityRecordResult>, CreateEntityRecordHandler>();
        services.AddScoped<ICommandHandler<UpdateEntityRecordCommand, int>, UpdateEntityRecordHandler>();
        services.AddScoped<ICommandHandler<DeleteEntityRecordCommand, int>, DeleteEntityRecordHandler>();
        services.AddScoped<IQueryHandler<RelatedRecordLinksForRowQuery, IReadOnlyList<RecordLink>>, RelatedRecordLinksForRowHandler>();
        services.AddScoped<ICommandHandler<SaveSqlChartCommand, ProjectChartView>, SaveSqlChartHandler>();
        services.AddScoped<ICommandHandler<DeleteSqlChartCommand, bool>, DeleteSqlChartHandler>();
        return services;
    }

    public static IServiceCollection AddDataModule(this IServiceCollection services)
    {
        services.TryAddSingleton<AnalyticsRefreshQueue>();
        services.AddSingleton<DecisionTreeAssembler>();
        services.AddScoped<DecisionTreeProvider>();
        services.AddScoped<IUncachedDecisionTreeProvider>(provider =>
            provider.GetRequiredService<DecisionTreeProvider>());
        services.AddScoped<IDecisionTreeProvider>(provider =>
            provider.GetRequiredService<DecisionTreeProvider>());
        services.AddScoped<DataMappingIngestionService>();
        services.AddScoped<EntityTagService>();
        services.AddScoped<RecordLinkService>();
        services.AddScoped<ProjectChartService>();
        services.AddScoped<IProjectChartRefresher>(provider =>
            provider.GetRequiredService<ProjectChartService>());

        services.AddScoped<ICommandHandler<RebuildGraphCommand, GraphRebuildResult>, RebuildGraphHandler>();
        services.AddScoped<IQueryHandler<QueryGraphQuery, GraphQueryView>, QueryGraphHandler>();
        services.AddScoped<IQueryHandler<GetGraphVizQuery, GraphVizView>, GetGraphVizHandler>();
        services.AddScoped<ICommandHandler<SaveSavedQueryCommand, SavedQueryRecord>, SaveSavedQueryHandler>();
        services.AddScoped<ICommandHandler<DeleteSavedQueryCommand, bool>, DeleteSavedQueryHandler>();
        services.AddScoped<IQueryHandler<ListSavedQueriesQuery, IReadOnlyList<SavedQueryRecord>>, ListSavedQueriesHandler>();
        services.AddScoped<ICommandHandler<RescanRecordLinksCommand, RecordLinkRescanResult>, RescanRecordLinksHandler>();
        services.AddScoped<IQueryHandler<ListRecordLinkGroupsQuery, IReadOnlyList<RecordLinkGroup>>, ListRecordLinkGroupsHandler>();
        services.AddScoped<IQueryHandler<RelatedRecordLinksQuery, IReadOnlyList<RecordLink>>, RelatedRecordLinksHandler>();
        services.AddScoped<IQueryHandler<RelatedRecordLinksForRowQuery, IReadOnlyList<RecordLink>>, RelatedRecordLinksForRowHandler>();

        services.AddScoped<ICommandHandler<SaveDataMappingCommand, DataMappingView>, SaveDataMappingHandler>();
        services.AddScoped<ICommandHandler<DeleteDataMappingCommand, bool>, DeleteDataMappingHandler>();
        services.AddScoped<IQueryHandler<ListDataMappingsQuery, IReadOnlyList<DataMappingView>>, ListDataMappingsHandler>();
        services.AddScoped<ICommandHandler<SaveDataEntityCommand, DataEntityView>, SaveDataEntityHandler>();
        services.AddScoped<ICommandHandler<DeleteDataEntityCommand, bool>, DeleteDataEntityHandler>();
        services.AddScoped<IQueryHandler<ListDataEntitiesQuery, IReadOnlyList<DataEntityView>>, ListDataEntitiesHandler>();
        services.AddScoped<IQueryHandler<TaggedRunsQuery, IReadOnlyList<Guid>>, TaggedRunsHandler>();
        services.AddScoped<IQueryHandler<EntityRunsQuery, IReadOnlyList<Guid>>, EntityRunsHandler>();
        services.AddScoped<IQueryHandler<EntityTagPairsQuery, IReadOnlyList<EntityTagPair>>, EntityTagPairsHandler>();
        services.AddScoped<ICommandHandler<SaveSqlChartCommand, ProjectChartView>, SaveSqlChartHandler>();
        services.AddScoped<ICommandHandler<DeleteSqlChartCommand, bool>, DeleteSqlChartHandler>();
        services.AddScoped<ICommandHandler<SaveProjectViewCommand, bool>, SaveProjectViewHandler>();
        services.AddScoped<ICommandHandler<DropProjectViewCommand, bool>, DropProjectViewHandler>();
        services.AddScoped<ICommandHandler<CreateEntityRecordCommand, CreateEntityRecordResult>, CreateEntityRecordHandler>();
        services.AddScoped<ICommandHandler<UpdateEntityRecordCommand, int>, UpdateEntityRecordHandler>();
        services.AddScoped<ICommandHandler<DeleteEntityRecordCommand, int>, DeleteEntityRecordHandler>();
        services.AddScoped<ICommandHandler<ExecuteProjectDataCommand, ProjectQueryResult>, ExecuteProjectDataHandler>();
        services.AddScoped<IQueryHandler<ListProjectDataTablesQuery, IReadOnlyList<ProjectTableInfo>>, ListProjectDataTablesHandler>();
        services.AddScoped<IQueryHandler<QueryProjectTablePageQuery, ProjectTablePageResult>, QueryProjectTablePageHandler>();
        services.AddScoped<ICommandHandler<CreateProjectTableCommand, bool>, CreateProjectTableHandler>();
        services.AddScoped<ICommandHandler<ImportCsvToProjectTableCommand, ImportCsvResult>, ImportCsvToProjectTableHandler>();
        services.AddScoped<ICommandHandler<MaterializeTableIndexCommand, MaterializeTableIndexResult>, MaterializeTableIndexHandler>();
        services.AddScoped<ICommandHandler<RenameProjectTableCommand, bool>, RenameProjectTableHandler>();
        services.AddScoped<ICommandHandler<DropProjectTableCommand, bool>, DropProjectTableHandler>();
        services.AddScoped<IQueryHandler<ExportProjectTableQuery, string>, ExportProjectTableHandler>();
        services.AddScoped<IQueryHandler<ListProjectTableColumnsQuery, IReadOnlyList<ProjectColumnInfo>>, ListProjectTableColumnsHandler>();
        services.AddScoped<ICommandHandler<AddProjectTableColumnCommand, bool>, AddProjectTableColumnHandler>();
        services.AddScoped<ICommandHandler<DropProjectTableColumnCommand, bool>, DropProjectTableColumnHandler>();
        services.AddScoped<ICommandHandler<GenerateProjectChartCommand, string>, GenerateProjectChartHandler>();
        services.AddScoped<IQueryHandler<ListProjectChartsQuery, IReadOnlyList<ProjectChartView>>, ListProjectChartsHandler>();
        return services;
    }
}
