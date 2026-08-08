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

        // Resolve each distinct job and project once; runs of deleted jobs/projects still report.
        var names = new Dictionary<Guid, string>();
        foreach (var jobId in runs.Select(r => r.JobId).Distinct())
        {
            var job = await _jobs.GetByIdAsync(jobId, ct);
            names[jobId] = job?.Name ?? "(deleted job)";
        }
        var projectNames = new Dictionary<Guid, string>();
        foreach (var projectId in runs.Select(r => r.ProjectId).Distinct())
        {
            var project = await _projects.GetByIdAsync(Domain.ValueObjects.ProjectId.From(projectId), ct);
            projectNames[projectId] = project?.Name.Value ?? "(deleted project)";
        }

        return runs
            .Select(r => new RunReportView(r.JobId, names[r.JobId], projectNames[r.ProjectId], JobViewMapper.ToDetailView(r)))
            .ToList();
    }
}
