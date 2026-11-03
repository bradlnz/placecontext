using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
namespace PlaceContext.Application.Features;

public sealed record GetSearchLastUpdatedQuery(
    Guid ProjectId, string IndexPattern, IReadOnlyList<string> CandidateFields)
    : IQuery<SearchLastUpdatedView>;
