using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Jobs;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class GetJobTestCaseHandler
    : IQueryHandler<GetJobTestCaseQuery, JobTestCaseView?>
{
    private readonly IJobTestStore _tests;
    private readonly IJobRepository _jobs;

    public GetJobTestCaseHandler(IJobTestStore tests, IJobRepository jobs)
        => (_tests, _jobs) = (tests, jobs);

    public async Task<JobTestCaseView?> HandleAsync(
        GetJobTestCaseQuery query, CancellationToken ct = default)
    {
        var test = await _tests.GetAsync(query.TestId, ct);
        if (test is null)
        {
            return null;
        }
        var job = await _jobs.GetByIdAsync(test.JobId, ct);
        return SaveJobTestCaseHandler.ToView(test, job?.Name ?? "Deleted job");
    }
}
