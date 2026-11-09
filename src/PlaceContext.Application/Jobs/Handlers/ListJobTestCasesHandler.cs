using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Jobs;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListJobTestCasesHandler
    : IQueryHandler<ListJobTestCasesQuery, IReadOnlyList<JobTestCaseView>>
{
    private readonly IJobTestStore _tests;
    private readonly IJobRepository _jobs;

    public ListJobTestCasesHandler(IJobTestStore tests, IJobRepository jobs)
        => (_tests, _jobs) = (tests, jobs);

    public async Task<IReadOnlyList<JobTestCaseView>> HandleAsync(
        ListJobTestCasesQuery query, CancellationToken ct = default)
    {
        var jobs = (await _jobs.ListForProjectAsync(query.ProjectId, ct))
            .ToDictionary(job => job.Id, job => job.Name);
        return (await _tests.ListForProjectAsync(query.ProjectId, ct))
            .Select(test => SaveJobTestCaseHandler.ToView(
                test, jobs.GetValueOrDefault(test.JobId, "Deleted job")))
            .ToList();
    }
}
