using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class GetJobRunHandler : IQueryHandler<GetJobRunQuery, JobRunDetailView?>
{
    private readonly IJobRunRepository _runs;

    public GetJobRunHandler(IJobRunRepository runs) => _runs = runs;

    public async Task<JobRunDetailView?> HandleAsync(GetJobRunQuery query, CancellationToken ct = default)
    {
        var run = await _runs.GetByIdAsync(query.RunId, ct);
        return run is null ? null : JobViewMapper.ToDetailView(run);
    }
}
