using System.Text.Json.Nodes;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.ProjectData;
using PlaceContext.Application.Shared;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class SaveSqlChartHandler : ICommandHandler<SaveSqlChartCommand, ProjectChartView>
{
    /// <summary>Chart-slot prefix that marks a chart as SQL-defined (never a real table name).</summary>
    public const string Prefix = "sql:";

    private readonly IProjectDataReader _store;
    private readonly IProjectChartRepository _charts;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public SaveSqlChartHandler(IProjectDataReader store, IProjectChartRepository charts, IUnitOfWork uow, IClock clock)
    {
        _store = store;
        _charts = charts;
        _uow = uow;
        _clock = clock;
    }

    public async Task<ProjectChartView> HandleAsync(SaveSqlChartCommand command, CancellationToken ct = default)
    {
        var name = command.Name.Trim();
        if (name.Length is 0 or > 80)
        {
            throw new ArgumentException("Give the chart a name (max 80 chars).");
        }
        if (string.IsNullOrWhiteSpace(command.Sql))
        {
            throw new ArgumentException("A SQL query is required.");
        }
        var chartType = (command.ChartType ?? "bar").Trim().ToLowerInvariant();
        if (chartType is not ("bar" or "line" or "pie"))
        {
            throw new ArgumentException("Chart type must be bar, line, or pie.");
        }
        var result = await _store.QueryAsync(command.ProjectId, command.Sql, ct);
        var html = SqlChartDefinition.Build(name, command.Sql, chartType, result);

        var slot = Prefix + name;
        var chart = ProjectChart.Create(command.ProjectId, slot, html, _clock.UtcNow);
        await _charts.UpsertAsync(chart, ct);
        await _uow.SaveChangesAsync(ct);
        return new ProjectChartView(slot, chart.Html, chart.GeneratedAt, IsLive: true);
    }
}
