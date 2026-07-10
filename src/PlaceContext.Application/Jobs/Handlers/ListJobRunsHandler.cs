using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListJobRunsHandler : IQueryHandler<ListJobRunsQuery, IReadOnlyList<JobRunView>>
{
    private readonly IJobRunRepository _runs;

    public ListJobRunsHandler(IJobRunRepository runs) => _runs = runs;

    public async Task<IReadOnlyList<JobRunView>> HandleAsync(ListJobRunsQuery query, CancellationToken ct = default)
    {
        var runs = await _runs.ListForJobAsync(query.JobId, ct);
        return runs
            .OrderByDescending(r => r.StartedAt)
            .Select(JobViewMapper.ToSummaryView)
            .ToList();
    }
}
