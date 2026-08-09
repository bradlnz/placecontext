using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Analytics;
using PlaceContext.Data.Contracts.Api;

namespace PlaceContext.Data.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/analytics")]
[Authorize(Policy = Permission.DataRead)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ProjectAnalyticsController(
    IDispatcher dispatcher,
    ICurrentTenant currentTenant,
    AnalyticsRefreshQueue refreshQueue) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AnalyticsPageResponse>> Get(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var tablesTask = dispatcher.Query(new ListProjectDataTablesQuery(projectId), cancellationToken);
        var chartsTask = dispatcher.Query(new ListProjectChartsQuery(projectId), cancellationToken);
        await Task.WhenAll(tablesTask, chartsTask);
        var tables = await tablesTask;
        return Ok(new AnalyticsPageResponse(
            tables.Select(table => new AnalyticsTableResponse(table.Name, table.RowEstimate)).ToList(),
            (await chartsTask).Select(MapChart).ToList(),
            refreshQueue.IsPending(projectId),
            tables
                .Where(table => refreshQueue.IsPending(projectId, table.Name))
                .Select(table => table.Name)
                .ToList()));
    }

    [HttpPost("refreshes")]
    public ActionResult<AnalyticsMessageResponse> QueueRefresh(
        Guid projectId,
        [FromBody] QueueAnalyticsRefreshRequest request)
    {
        if (!currentTenant.IsResolved)
            return Unauthorized(new { error = "No tenant resolved — sign in again." });

        var queued = refreshQueue.TryEnqueue(
            new TenantContext(currentTenant.TenantId, currentTenant.Slug, currentTenant.TimeZoneId),
            projectId,
            tableName: string.IsNullOrWhiteSpace(request.TableName) ? null : request.TableName,
            instruction: request.Instruction);
        return Accepted(new AnalyticsMessageResponse(
            queued ? "Chart generation queued." : "That chart generation is already pending."));
    }

    [HttpPut("sql-charts")]
    public async Task<ActionResult<AnalyticsChartResponse>> SaveSqlChart(
        Guid projectId,
        [FromBody] SaveSqlChartRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "Chart name is required." });
        if (string.IsNullOrWhiteSpace(request.Sql)) return BadRequest(new { error = "SQL query is required." });
        if (request.ChartType is not ("bar" or "line" or "pie")) return BadRequest(new { error = "Chart type must be bar, line, or pie." });
        try
        {
            return Ok(MapChart(await dispatcher.Send(new SaveSqlChartCommand(
                projectId, request.Name.Trim(), request.Sql, request.ChartType), cancellationToken)));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpDelete("sql-charts/{name}")]
    public async Task<IActionResult> DeleteSqlChart(Guid projectId, string name, CancellationToken cancellationToken) =>
        await dispatcher.Send(new DeleteSqlChartCommand(projectId, name), cancellationToken)
            ? NoContent()
            : NotFound(new { error = "The SQL chart does not exist." });

    private AnalyticsChartResponse MapChart(ProjectChartView chart)
    {
        JsonNode? spec = null;
        try { spec = JsonNode.Parse(chart.Html); } catch { }
        return new AnalyticsChartResponse(
            chart.TableName,
            chart.TableName.StartsWith("sql:", StringComparison.Ordinal) ? chart.TableName[4..] : chart.TableName,
            chart.GeneratedAt,
            ToWorkspaceTime(chart.GeneratedAt).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            spec,
            spec is null ? chart.Html : null,
            spec?["sql"]?.GetValue<string>(),
            spec?["type"]?.GetValue<string>() ?? "bar");
    }

    private DateTimeOffset ToWorkspaceTime(DateTimeOffset value)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById(currentTenant.TimeZoneId));
        }
        catch
        {
            return value;
        }
    }
}
