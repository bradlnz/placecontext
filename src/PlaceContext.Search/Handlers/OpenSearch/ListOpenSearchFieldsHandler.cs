using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class ListOpenSearchFieldsHandler
    : IQueryHandler<ListOpenSearchFieldsQuery, IReadOnlyList<OpenSearchFieldView>>
{
    private readonly IOpenSearchDataGateway _gateway;
    public ListOpenSearchFieldsHandler(IOpenSearchDataGateway gateway) => _gateway = gateway;
    public Task<IReadOnlyList<OpenSearchFieldView>> HandleAsync(
        ListOpenSearchFieldsQuery query, CancellationToken ct = default)
        => _gateway.ListFieldsAsync(query.ProjectId, query.IndexPattern, ct);
}
