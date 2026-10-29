using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record ListOpenSearchFieldsQuery(Guid ProjectId, string IndexPattern)
    : IQuery<IReadOnlyList<OpenSearchFieldView>>;
