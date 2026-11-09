using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Tenant-wide cost analysis across all projects.</summary>
public sealed record GetRootCostQuery : IQuery<RootCostView>;
