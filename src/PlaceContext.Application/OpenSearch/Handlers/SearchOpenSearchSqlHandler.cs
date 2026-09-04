using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class SearchOpenSearchSqlHandler
    : IQueryHandler<SearchOpenSearchSqlQuery, ProjectQueryResult>
{
    private readonly IOpenSearchDataGateway _gateway;
    public SearchOpenSearchSqlHandler(IOpenSearchDataGateway gateway) => _gateway = gateway;
    public Task<ProjectQueryResult> HandleAsync(
        SearchOpenSearchSqlQuery query, CancellationToken ct = default)
        => _gateway.SearchSqlAsync(query.ProjectId, query.Sql, ct);
}
