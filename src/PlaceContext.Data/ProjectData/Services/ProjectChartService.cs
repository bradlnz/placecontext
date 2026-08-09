using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>
/// Draws one analytics chart per table in a project's database, deterministically from the data:
/// sample rows → a <see cref="ChartSpec"/> (data only; Chart.js draws it in the portal). A table
/// with nothing chartable stores a themed HTML table so the tab always renders something. The
/// Host's background worker runs <see cref="RefreshProjectAsync"/> and the Analytics tab reads the
/// stored results.
/// </summary>
public sealed class ProjectChartService : IProjectChartRefresher
{
    private const int SampleRows = 200;   // enough shape for a chart, bounded

    private readonly IProjectDataStore _store;
    private readonly IProjectChartRepository _charts;
    private readonly IDataUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<ProjectChartService>? _log;

    public ProjectChartService(IProjectDataStore store, IProjectChartRepository charts,
        IDataUnitOfWork uow, IClock clock, ILogger<ProjectChartService>? log = null)
    {
        _store = store;
        _charts = charts;
        _uow = uow;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Regenerate the stored chart for every table in the project's database and prune charts of
    /// dropped tables. Each table is isolated: one failure doesn't abandon the sweep. Saves as it
    /// goes, so finished charts appear in the portal while later tables are still rendering.
    /// </summary>
    public async Task RefreshProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var tables = await _store.ListTablesAsync(projectId, ct);

        await _charts.DeleteForProjectAsync(projectId, tables.Select(t => t.Name).ToList(), ct);
        await _uow.SaveChangesAsync(ct);

        foreach (var table in tables)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var html = await GenerateChartHtmlAsync(projectId, table.Name, instruction: null, ct);
                await _charts.UpsertAsync(ProjectChart.Create(projectId, table.Name, html, _clock.UtcNow), ct);
                await _uow.SaveChangesAsync(ct);
                _log?.LogInformation("Analytics: stored chart for {Table} (project {ProjectId}).", table.Name, projectId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log?.LogWarning(ex, "Analytics: chart for table {Table} (project {ProjectId}) failed — skipping.",
                    table.Name, projectId);
            }
        }
    }

    /// <summary>Regenerate one table's stored chart, optionally steered by an instruction.</summary>
    public async Task RefreshTableAsync(Guid projectId, string tableName, string? instruction, CancellationToken ct = default)
    {
        var html = await GenerateChartHtmlAsync(projectId, tableName, instruction, ct);
        await _charts.UpsertAsync(ProjectChart.Create(projectId, tableName, html, _clock.UtcNow), ct);
        await _uow.SaveChangesAsync(ct);
        _log?.LogInformation("Analytics: stored chart for {Table} (project {ProjectId}).", tableName, projectId);
    }

    /// <summary>One chart, returned (not stored): sample the table, ask the LLM, theme the result.</summary>
    public async Task<string> GenerateChartHtmlAsync(Guid projectId, string tableName, string? instruction, CancellationToken ct = default)
    {
        // The store validates + quotes the identifier on its own DDL paths; quote the same way here.
        var table = tableName.Replace("\"", "\"\"");
        var result = await _store.ExecuteAsync(projectId, $"SELECT * FROM \"{table}\" LIMIT {SampleRows}", ct);

        _ = instruction; // reserved for future steering; the spec is derived from the data itself

        // Build the spec from the data itself; a table with nothing chartable stores a themed HTML
        // table so the tab always renders something.
        if (ChartSpec.FromSample(tableName, result) is { } fallback)
            return fallback.ToJson();
        return LlmHtml.StyleChart(FallbackTable(tableName, result));
    }

    // Nothing chartable: the data itself, as a plain themed table, so the tab always renders.
    private static string FallbackTable(string tableName, ProjectQueryResult result)
    {
        var sb = new StringBuilder("<html><head></head><body>");
        sb.Append("<h1>").Append(Escape(tableName)).Append("</h1>");
        sb.Append("<p>Nothing chartable in this table — here is the sampled data itself.</p><table><thead><tr>");
        foreach (var col in result.Columns) sb.Append("<th>").Append(Escape(col)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var row in result.Rows.Take(50))
        {
            sb.Append("<tr>");
            foreach (var cell in row) sb.Append("<td>").Append(Escape(cell ?? "∅")).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table></body></html>");
        return sb.ToString();
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
