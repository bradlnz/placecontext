using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>A chain's run history, newest first — the pipeline view's list.</summary>
public sealed record ListChainRunsQuery(Guid ChainId, int Take = 20) : IQuery<IReadOnlyList<ChainRunView>>;
