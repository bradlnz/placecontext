using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListRecentChainRunsHandler : IQueryHandler<ListRecentChainRunsQuery, IReadOnlyList<ChainRunReportView>>
{
    private readonly IChainRunRepository _runs;
    private readonly IProjectRepository _projects;

    public ListRecentChainRunsHandler(IChainRunRepository runs, IProjectRepository projects)
    {
        _runs = runs;
        _projects = projects;
    }

    public async Task<IReadOnlyList<ChainRunReportView>> HandleAsync(ListRecentChainRunsQuery query, CancellationToken ct = default)
    {
        var runs = await _runs.ListRecentAsync(query.Take, ct);

        var projectNames = new Dictionary<Guid, string>();
        foreach (var projectId in runs.Select(r => r.ProjectId).Distinct())
        {
            var project = await _projects.GetByIdAsync(Domain.ValueObjects.ProjectId.From(projectId), ct);
            projectNames[projectId] = project?.Name.Value ?? "(deleted project)";
        }

        return runs
            .Select(r => new ChainRunReportView(r.ProjectId, projectNames[r.ProjectId], ChainRunMapper.ToView(r)))
            .ToList();
    }
}
