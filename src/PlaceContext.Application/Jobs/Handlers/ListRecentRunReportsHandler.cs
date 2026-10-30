using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListRecentRunReportsHandler : IQueryHandler<ListRecentRunReportsQuery, IReadOnlyList<RunReportView>>
{
    private readonly IJobRunRepository _runs;
    private readonly IJobRepository _jobs;
    private readonly IProjectRepository _projects;

    public ListRecentRunReportsHandler(IJobRunRepository runs, IJobRepository jobs, IProjectRepository projects)
    {
        _runs = runs;
        _jobs = jobs;
        _projects = projects;
    }

    public async Task<IReadOnlyList<RunReportView>> HandleAsync(ListRecentRunReportsQuery query, CancellationToken ct = default)
    {
        var runs = await _runs.ListRecentAsync(query.Take, ct);

        if (runs.Count == 0) return [];
        var names = await _jobs.GetNamesAsync(runs.Select(r => r.JobId).Distinct().ToArray(), ct);
        var projectNames = await _projects.GetNamesAsync(runs.Select(r => r.ProjectId).Distinct().ToArray(), ct);

        return runs
            .Select(r => new RunReportView(r.JobId, names.GetValueOrDefault(r.JobId, "(deleted job)"),
                projectNames.GetValueOrDefault(r.ProjectId, "(deleted project)"), JobViewMapper.ToDetailView(r)))
            .ToList();
    }
}
