using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record ListOpenSearchDashboardsQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<OpenSearchDashboardView>>;
