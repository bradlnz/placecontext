using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>One chain run with live per-step statuses — polled while the pipeline is running.</summary>
public sealed record GetChainRunQuery(Guid ChainRunId) : IQuery<ChainRunView?>;
