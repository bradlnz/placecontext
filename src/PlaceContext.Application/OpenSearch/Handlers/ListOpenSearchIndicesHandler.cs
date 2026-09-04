using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class ListOpenSearchIndicesHandler
    : IQueryHandler<ListOpenSearchIndicesQuery, IReadOnlyList<OpenSearchIndexView>>
{
    private readonly IOpenSearchDataGateway _gateway;
    public ListOpenSearchIndicesHandler(IOpenSearchDataGateway gateway) => _gateway = gateway;
    public Task<IReadOnlyList<OpenSearchIndexView>> HandleAsync(
        ListOpenSearchIndicesQuery query, CancellationToken ct = default)
        => _gateway.ListIndicesAsync(query.ProjectId, ct);
}
