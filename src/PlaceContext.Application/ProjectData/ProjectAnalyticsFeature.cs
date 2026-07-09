using PlaceContext.Application.Cqrs;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>One stored analytics chart, ready to render (self-contained themed HTML document).</summary>
public sealed record ProjectChartView(string TableName, string Html, DateTimeOffset GeneratedAt);

/// <summary>
/// Draw a chart over one table of the project's database, synchronously, and return the HTML.
/// Slow on CPU (minutes) — the portal uses the background sweep + stored charts instead; this
/// remains for programmatic/MCP callers that want a one-off chart.
/// </summary>
public sealed record GenerateProjectChartCommand(Guid ProjectId, string TableName, string? Instruction) : ICommand<string>;

/// <summary>The project's stored analytics charts (produced by the background sweep).</summary>
public sealed record ListProjectChartsQuery(Guid ProjectId) : IQuery<IReadOnlyList<ProjectChartView>>;

public sealed class GenerateProjectChartHandler : ICommandHandler<GenerateProjectChartCommand, string>
{
    private readonly IProjectRepository _projects;
    private readonly ProjectChartService _charts;

    public GenerateProjectChartHandler(IProjectRepository projects, ProjectChartService charts)
    {
        _projects = projects;
        _charts = charts;
    }

    public async Task<string> HandleAsync(GenerateProjectChartCommand c, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, c.ProjectId, ct);
        return await _charts.GenerateChartHtmlAsync(c.ProjectId, c.TableName, c.Instruction, ct);
    }
}

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
