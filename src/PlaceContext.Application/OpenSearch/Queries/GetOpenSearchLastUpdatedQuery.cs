using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record GetOpenSearchLastUpdatedQuery(
    Guid ProjectId, string IndexPattern, IReadOnlyList<string> CandidateFields)
    : IQuery<OpenSearchLastUpdatedView>;
