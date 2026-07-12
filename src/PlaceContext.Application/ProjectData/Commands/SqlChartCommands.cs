using System.Text.Json.Nodes;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>
/// Creates or refreshes a user-defined SQL chart: the query runs INSIDE the project's database —
/// as the project's own Postgres role with search_path pinned to its schema — so it can only ever
/// see that project's data. The result is folded into a chart spec (first text column = labels,
/// numeric columns = series) and stored under the reserved <c>sql:{name}</c> chart slot with the
/// query embedded, so it can be re-run and edited later.
/// </summary>
public sealed record SaveSqlChartCommand(Guid ProjectId, string Name, string Sql, string ChartType = "bar") : ICommand<ProjectChartView>;

/// <summary>Removes a user-defined SQL chart.</summary>
public sealed record DeleteSqlChartCommand(Guid ProjectId, string Name) : ICommand<bool>;

public sealed class SaveSqlChartHandler : ICommandHandler<SaveSqlChartCommand, ProjectChartView>
{
    /// <summary>Chart-slot prefix that marks a chart as SQL-defined (never a real table name).</summary>
    public const string Prefix = "sql:";

    private readonly IProjectDataStore _store;
    private readonly IProjectChartRepository _charts;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public SaveSqlChartHandler(IProjectDataStore store, IProjectChartRepository charts, IUnitOfWork uow, IClock clock)
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
            throw new ArgumentException("Give the chart a name (max 80 chars).");
        if (string.IsNullOrWhiteSpace(command.Sql))
            throw new ArgumentException("A SQL query is required.");

        // Isolated by construction: ExecuteAsync runs as the project role in the project schema.
        var result = await _store.ExecuteAsync(command.ProjectId, command.Sql, ct);
        var spec = ChartSpec.FromSample(name, result)
            ?? throw new InvalidOperationException(
                "The query result isn't chartable — return at least one label column and one numeric column.");

        // Embed the query in the stored spec (extra JSON properties are ignored by the renderer)
        // so the chart stays editable and refreshable, and honour the requested form — switching
        // a chart from bar to pie is a re-save with the same SQL and a different type.
        var chartType = (command.ChartType ?? "bar").Trim().ToLowerInvariant();
        if (chartType is not ("bar" or "line" or "pie"))
            throw new ArgumentException("Chart type must be bar, line, or pie.");
        var node = JsonNode.Parse(spec.ToJson())!;
        node["type"] = chartType;
        node["sql"] = command.Sql;

        var slot = Prefix + name;
        var chart = ProjectChart.Create(command.ProjectId, slot, node.ToJsonString(), _clock.UtcNow);
        await _charts.UpsertAsync(chart, ct);
        await _uow.SaveChangesAsync(ct);
        return new ProjectChartView(slot, chart.Html, chart.GeneratedAt);
    }
}

public sealed class DeleteSqlChartHandler : ICommandHandler<DeleteSqlChartCommand, bool>
{
    private readonly IProjectChartRepository _charts;
    private readonly IUnitOfWork _uow;

    public DeleteSqlChartHandler(IProjectChartRepository charts, IUnitOfWork uow)
    {
        _charts = charts;
        _uow = uow;
    }

    public async Task<bool> HandleAsync(DeleteSqlChartCommand command, CancellationToken ct = default)
    {
        await _charts.DeleteAsync(command.ProjectId, SaveSqlChartHandler.Prefix + command.Name.Trim(), ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
