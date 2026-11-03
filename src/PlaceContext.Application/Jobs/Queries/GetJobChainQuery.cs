using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record GetJobChainQuery(Guid ChainId) : IQuery<JobChainView?>;
