using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Search;
namespace PlaceContext.Application.Features;

public sealed class ListSearchIndicesHandler
    : IQueryHandler<ListSearchIndicesQuery, IReadOnlyList<SearchIndexView>>
{
    private readonly ISearchDataGateway _gateway;
    public ListSearchIndicesHandler(ISearchDataGateway gateway) => _gateway = gateway;
    public Task<IReadOnlyList<SearchIndexView>> HandleAsync(
        ListSearchIndicesQuery query, CancellationToken ct = default)
        => _gateway.ListIndicesAsync(query.ProjectId, ct);
}
