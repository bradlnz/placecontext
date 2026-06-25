using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListJobsHandler : IQueryHandler<ListJobsQuery, IReadOnlyList<JobView>>
{
    private readonly IJobRepository _jobs;

    public ListJobsHandler(IJobRepository jobs) => _jobs = jobs;

    public async Task<IReadOnlyList<JobView>> HandleAsync(ListJobsQuery query, CancellationToken ct = default)
    {
        var jobs = await _jobs.ListForProjectAsync(query.ProjectId, ct);
        return jobs.Select(JobViewMapper.ToView).ToList();
    }
}
