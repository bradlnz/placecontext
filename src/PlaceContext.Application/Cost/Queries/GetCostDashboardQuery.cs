using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>A project's token/cost dashboard.</summary>
public sealed record GetCostDashboardQuery(Guid ProjectId) : IQuery<CostDashboardView>;
