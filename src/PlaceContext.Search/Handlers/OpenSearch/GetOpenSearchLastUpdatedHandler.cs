using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class GetOpenSearchLastUpdatedHandler
    : IQueryHandler<GetOpenSearchLastUpdatedQuery, OpenSearchLastUpdatedView>
{
    private readonly IOpenSearchDataGateway _gateway;
    public GetOpenSearchLastUpdatedHandler(IOpenSearchDataGateway gateway) => _gateway = gateway;
    public Task<OpenSearchLastUpdatedView> HandleAsync(
        GetOpenSearchLastUpdatedQuery query, CancellationToken ct = default)
        => _gateway.GetLastUpdatedAsync(
            query.ProjectId, query.IndexPattern, query.CandidateFields, ct);
}
