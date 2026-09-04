using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class SearchOpenSearchHandler
    : IQueryHandler<SearchOpenSearchQuery, OpenSearchSearchView>
{
    private readonly IOpenSearchDataGateway _gateway;
    public SearchOpenSearchHandler(IOpenSearchDataGateway gateway) => _gateway = gateway;
    public Task<OpenSearchSearchView> HandleAsync(
        SearchOpenSearchQuery query, CancellationToken ct = default)
        => _gateway.SearchAsync(query.Request, ct);
}
