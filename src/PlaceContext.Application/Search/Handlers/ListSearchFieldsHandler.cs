using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Search;
namespace PlaceContext.Application.Features;

public sealed class ListSearchFieldsHandler
    : IQueryHandler<ListSearchFieldsQuery, IReadOnlyList<SearchFieldView>>
{
    private readonly ISearchDataGateway _gateway;
    public ListSearchFieldsHandler(ISearchDataGateway gateway) => _gateway = gateway;
    public Task<IReadOnlyList<SearchFieldView>> HandleAsync(
        ListSearchFieldsQuery query, CancellationToken ct = default)
        => _gateway.ListFieldsAsync(query.ProjectId, query.IndexPattern, ct);
}
