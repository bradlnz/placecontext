using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Search;
namespace PlaceContext.Application.Features;

public sealed class GetSearchLastUpdatedHandler
    : IQueryHandler<GetSearchLastUpdatedQuery, SearchLastUpdatedView>
{
    private readonly ISearchDataGateway _gateway;
    public GetSearchLastUpdatedHandler(ISearchDataGateway gateway) => _gateway = gateway;
    public Task<SearchLastUpdatedView> HandleAsync(
        GetSearchLastUpdatedQuery query, CancellationToken ct = default)
        => _gateway.GetLastUpdatedAsync(
            query.ProjectId, query.IndexPattern, query.CandidateFields, ct);
}
