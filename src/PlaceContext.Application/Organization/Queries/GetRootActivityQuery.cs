using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

// ---- Change Activity (cross-project feed) ----

public sealed record GetRootActivityQuery(int Take = 40) : IQuery<RootActivityView>;
