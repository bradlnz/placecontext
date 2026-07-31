using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record ListOpenSearchIndicesQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<OpenSearchIndexView>>;

public sealed record ListOpenSearchFieldsQuery(Guid ProjectId, string IndexPattern)
    : IQuery<IReadOnlyList<OpenSearchFieldView>>;

public sealed record GetOpenSearchLastUpdatedQuery(
    Guid ProjectId, string IndexPattern, IReadOnlyList<string> CandidateFields)
    : IQuery<OpenSearchLastUpdatedView>;

public sealed record SearchOpenSearchQuery(OpenSearchSearchRequest Request)
    : IQuery<OpenSearchSearchView>;

public sealed record ListOpenSearchDashboardsQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<OpenSearchDashboardView>>;
