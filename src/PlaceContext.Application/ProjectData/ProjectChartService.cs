using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>
/// Draws analytics charts over a project's database with the local LLM. Generation on CPU takes
/// minutes per chart, so the portal never calls this synchronously: the Host's background worker
/// runs <see cref="RefreshProjectAsync"/> (all tables, stored via <see cref="IProjectChartRepository"/>)
/// and the Analytics tab reads the stored results. Per table: sample rows → LLM → themed HTML;
/// when the LLM is off or replies unusably the stored chart is a themed table of the data itself.
/// </summary>
public sealed class ProjectChartService
{
    private const int SampleRows = 200;   // enough shape for a chart, bounded for the model
    private const int MaxPromptChars = 12000;

    private readonly IProjectDataStore _store;
    private readonly IProjectChartRepository _charts;
    private readonly ILlmGateway _llm;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<ProjectChartService>? _log;

    public ProjectChartService(IProjectDataStore store, IProjectChartRepository charts, ILlmGateway llm,
        IUnitOfWork uow, IClock clock, ILogger<ProjectChartService>? log = null)
    {
        _store = store;
        _charts = charts;
        _llm = llm;
        _uow = uow;
        _clock = clock;
        _log = log;
    }

    private const string SystemPrompt =
        "You are given rows sampled from one database table, as JSON with 'columns' and 'rows'. " +
        "Produce a SINGLE, complete, self-contained HTML5 document whose body is ONE chart that best " +
        "visualizes this data — a bar, line, or pie chart drawn as inline <svg> (use numeric fields, " +
        "or counts of categorical values, whichever the data supports). If the request names a " +
        "specific view of the data, chart that. Give it a title, label the axes or include a legend, " +
        "and print the actual values as text on the chart. Use ONLY inline <svg> and minimal inline " +
        "CSS: no external stylesheets, scripts, images, fonts, or CDNs. Be faithful to the data; do " +
        "not invent values. If the data carries no chartable quantities, render a small summary table " +
        "instead. Do NOT set a page background or text colour and do NOT use a serif font — the " +
        "container themes those; just give bars/segments/slices distinct fill colours. " +
        "Output ONLY the HTML — no markdown fences, no commentary before or after.";

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

        var payload = JsonSerializer.Serialize(new
        {
            table = tableName,
            request = string.IsNullOrWhiteSpace(instruction) ? null : instruction!.Trim(),
            columns = result.Columns,
            rows = result.Rows,
        });

        if (_llm.IsEnabled)
        {
            try
            {
                var raw = await _llm.GenerateAsync(SystemPrompt, LlmHtml.Truncate(payload, MaxPromptChars), ct);
                var html = LlmHtml.ExtractHtml(raw);
                if (LlmHtml.LooksLikeHtml(html))
                    return LlmHtml.StyleChart(html);
                _log?.LogWarning("Analytics chart for table {Table} wasn't usable HTML — falling back to the data table.", tableName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log?.LogWarning(ex, "Analytics chart LLM call failed for table {Table} — falling back to the data table.", tableName);
            }
        }
        return LlmHtml.StyleChart(FallbackTable(tableName, result));
    }

    // No LLM (or it misbehaved): the data itself, as a plain themed table, so the tab always renders.
    private static string FallbackTable(string tableName, ProjectQueryResult result)
    {
        var sb = new StringBuilder("<html><head></head><body>");
        sb.Append("<h1>").Append(Escape(tableName)).Append("</h1>");
        sb.Append("<p>The local LLM is not available, so here is the sampled data itself.</p><table><thead><tr>");
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
