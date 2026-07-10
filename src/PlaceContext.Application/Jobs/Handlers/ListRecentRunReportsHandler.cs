using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListRecentRunReportsHandler : IQueryHandler<ListRecentRunReportsQuery, IReadOnlyList<RunReportView>>
{
    private readonly IJobRunRepository _runs;
    private readonly IJobRepository _jobs;

    public ListRecentRunReportsHandler(IJobRunRepository runs, IJobRepository jobs)
    {
        _runs = runs;
        _jobs = jobs;
    }

    public async Task<IReadOnlyList<RunReportView>> HandleAsync(ListRecentRunReportsQuery query, CancellationToken ct = default)
    {
        var runs = await _runs.ListRecentAsync(query.Take, ct);

        // Resolve each distinct job once; a run whose job has been deleted still reports.
        var names = new Dictionary<Guid, string>();
        foreach (var jobId in runs.Select(r => r.JobId).Distinct())
        {
            var job = await _jobs.GetByIdAsync(jobId, ct);
            names[jobId] = job?.Name ?? "(deleted job)";
        }

        return runs
            .Select(r => new RunReportView(r.JobId, names[r.JobId], JobViewMapper.ToDetailView(r)))
            .ToList();
    }
}
