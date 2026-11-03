using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.ProjectData;
using PlaceContext.Application.Search;
namespace PlaceContext.Application.Features;

public sealed class SearchSqlDocumentsHandler
    : IQueryHandler<SearchSqlDocumentsQuery, ProjectQueryResult>
{
    private readonly ISearchDataGateway _gateway;
    public SearchSqlDocumentsHandler(ISearchDataGateway gateway) => _gateway = gateway;
    public Task<ProjectQueryResult> HandleAsync(
        SearchSqlDocumentsQuery query, CancellationToken ct = default)
        => _gateway.SearchSqlAsync(query.ProjectId, query.Sql, ct);
}
