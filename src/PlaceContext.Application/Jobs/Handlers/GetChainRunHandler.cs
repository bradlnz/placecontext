using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class GetChainRunHandler : IQueryHandler<GetChainRunQuery, ChainRunView?>
{
    private readonly IChainRunRepository _runs;
    public GetChainRunHandler(IChainRunRepository runs) => _runs = runs;

    public async Task<ChainRunView?> HandleAsync(GetChainRunQuery query, CancellationToken ct = default)
    {
        var run = await _runs.GetByIdAsync(query.ChainRunId, ct);
        return run is null ? null : ChainRunMapper.ToView(run);
    }
}
