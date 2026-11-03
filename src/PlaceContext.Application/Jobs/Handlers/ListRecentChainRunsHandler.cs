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

        if (runs.Count == 0)
        {
            return [];
        }
        var projectNames = await _projects.GetNamesAsync(runs.Select(r => r.ProjectId).Distinct().ToArray(), ct);

        return runs
            .Select(r => new ChainRunReportView(r.ProjectId, projectNames.GetValueOrDefault(r.ProjectId, "(deleted project)"), ChainRunMapper.ToView(r)))
            .ToList();
    }
}
