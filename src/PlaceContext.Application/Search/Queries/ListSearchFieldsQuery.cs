using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
namespace PlaceContext.Application.Features;

public sealed record ListSearchFieldsQuery(Guid ProjectId, string IndexPattern)
    : IQuery<IReadOnlyList<SearchFieldView>>;
