using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class GetJobHandler : IQueryHandler<GetJobQuery, JobView?>
{
    private readonly IJobRepository _jobs;

    public GetJobHandler(IJobRepository jobs) => _jobs = jobs;

    public async Task<JobView?> HandleAsync(GetJobQuery query, CancellationToken ct = default)
    {
        var job = await _jobs.GetByIdAsync(query.JobId, ct);
        return job is null ? null : JobViewMapper.ToView(job);
    }
}
