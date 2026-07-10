using PlaceContext.Application.Cqrs;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

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
