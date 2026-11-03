using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
namespace PlaceContext.Application.Features;

public sealed record SearchDocumentsQuery(SearchResultRequest Request)
    : IQuery<SearchResultView>;
