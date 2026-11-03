using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Search;
namespace PlaceContext.Application.Features;

public sealed class SearchDocumentsHandler
    : IQueryHandler<SearchDocumentsQuery, SearchResultView>
{
    private readonly ISearchDataGateway _gateway;
    public SearchDocumentsHandler(ISearchDataGateway gateway) => _gateway = gateway;
    public Task<SearchResultView> HandleAsync(
        SearchDocumentsQuery query, CancellationToken ct = default)
        => _gateway.SearchAsync(query.Request, ct);
}
