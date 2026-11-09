using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

// ---- Root statistics strip (Overview) ----

public sealed record GetRootStatsQuery : IQuery<RootStatsView>;
