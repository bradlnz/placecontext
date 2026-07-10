using PlaceContext.Application.Cqrs;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListProjectChartsHandler : IQueryHandler<ListProjectChartsQuery, IReadOnlyList<ProjectChartView>>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectChartRepository _charts;

    public ListProjectChartsHandler(IProjectRepository projects, IProjectChartRepository charts)
    {
        _projects = projects;
        _charts = charts;
    }

    public async Task<IReadOnlyList<ProjectChartView>> HandleAsync(ListProjectChartsQuery q, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, q.ProjectId, ct);
        return (await _charts.ListForProjectAsync(q.ProjectId, ct))
            .Select(c => new ProjectChartView(c.TableName, c.Html, c.GeneratedAt))
            .ToList();
    }
}
