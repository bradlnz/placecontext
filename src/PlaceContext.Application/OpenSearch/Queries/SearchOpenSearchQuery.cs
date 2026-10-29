using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record SearchOpenSearchQuery(OpenSearchSearchRequest Request)
    : IQuery<OpenSearchSearchView>;
