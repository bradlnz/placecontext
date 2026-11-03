using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class GetJobChainHandler(IJobChainRepository chains, IJobRepository jobs) : IQueryHandler<GetJobChainQuery, JobChainView?>
{
    public async Task<JobChainView?> HandleAsync(GetJobChainQuery query, CancellationToken ct = default)
    {
        var chain = await chains.GetByIdAsync(query.ChainId, ct);
        return chain is null ? null : (await JobChainMapper.ToViewsAsync([chain], jobs, ct)).Single();
    }
}
