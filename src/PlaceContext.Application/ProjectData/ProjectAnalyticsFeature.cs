using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>
/// Draw a chart over one table of the project's database: a sample of the rows goes to the local
/// LLM (Ollama in-cluster), which returns a self-contained HTML+SVG chart, themed for the portal.
/// The optional instruction steers what the chart shows ("bookings per day", "totals by sensor").
/// Returns the HTML document; without a usable LLM the data comes back as a themed table instead.
/// </summary>
public sealed record GenerateProjectChartCommand(Guid ProjectId, string TableName, string? Instruction) : ICommand<string>;

public sealed class GenerateProjectChartHandler : ICommandHandler<GenerateProjectChartCommand, string>
{
    private const int SampleRows = 200;   // enough shape for a chart, bounded for the model
    private const int MaxPromptChars = 12000;

    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;
    private readonly ILlmGateway _llm;
    private readonly ILogger<GenerateProjectChartHandler>? _log;

    public GenerateProjectChartHandler(IProjectRepository projects, IProjectDataStore store, ILlmGateway llm,
        ILogger<GenerateProjectChartHandler>? log = null)
    {
        _projects = projects;
        _store = store;
        _llm = llm;
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

    public async Task<string> HandleAsync(GenerateProjectChartCommand c, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, c.ProjectId, ct);

        // The store validates + quotes the identifier on its own DDL paths; quote the same way here.
        var table = c.TableName.Replace("\"", "\"\"");
        var result = await _store.ExecuteAsync(c.ProjectId, $"SELECT * FROM \"{table}\" LIMIT {SampleRows}", ct);

        var payload = JsonSerializer.Serialize(new
        {
            table = c.TableName,
            request = string.IsNullOrWhiteSpace(c.Instruction) ? null : c.Instruction!.Trim(),
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
                _log?.LogWarning("Analytics chart for table {Table} wasn't usable HTML — falling back to the data table.", c.TableName);
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Analytics chart LLM call failed for table {Table} — falling back to the data table.", c.TableName);
            }
        }
        return LlmHtml.StyleChart(FallbackTable(c.TableName, result));
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
