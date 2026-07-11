using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>A chain's run history, newest first — the pipeline view's list.</summary>
public sealed record ListChainRunsQuery(Guid ChainId, int Take = 20) : IQuery<IReadOnlyList<ChainRunView>>;

/// <summary>One chain run with live per-step statuses — polled while the pipeline is running.</summary>
public sealed record GetChainRunQuery(Guid ChainRunId) : IQuery<ChainRunView?>;
