using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListJobChainsHandler : IQueryHandler<ListJobChainsQuery, IReadOnlyList<JobChainView>>
{
    private readonly IJobChainRepository _chains;
    private readonly IJobRepository _jobs;

    public ListJobChainsHandler(IJobChainRepository chains, IJobRepository jobs)
    {
        _chains = chains;
        _jobs = jobs;
    }

    public async Task<IReadOnlyList<JobChainView>> HandleAsync(ListJobChainsQuery query, CancellationToken ct = default)
    {
        var chains = await _chains.ListForProjectAsync(query.ProjectId, ct);
        var views = new List<JobChainView>(chains.Count);
        foreach (var chain in chains)
            views.Add(await JobChainMapper.ToViewAsync(chain, _jobs, ct));
        return views;
    }
}
