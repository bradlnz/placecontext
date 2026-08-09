using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Search.Controllers;

/// <summary>
/// Authenticated, constrained OpenSearch access. Credentials remain in the Search service;
/// callers can only use the bounded operations represented by Search-owned queries.
/// </summary>
[ApiController]
[Authorize(Policy = Permission.DataRead)]
[Route("api/v1/projects/{projectId:guid}/opensearch")]
public sealed class OpenSearchController(
    IDispatcher dispatcher,
    IAuthorizationService authorization) : ControllerBase
{
    [HttpGet("page")]
    public async Task<IActionResult> Page(
        Guid projectId,
        [FromQuery] string? index,
        CancellationToken ct)
    {
        var dashboardsTask = dispatcher.Query(new ListOpenSearchDashboardsQuery(projectId), ct);
        var canSyncTask = authorization.AuthorizeAsync(User, null, Permission.SettingsManage);
        try
        {
            var indices = await dispatcher.Query(new ListOpenSearchIndicesQuery(projectId), ct);
            var selectedIndex = SelectIndex(indices, index);
            if (selectedIndex is null)
                return Ok(new OpenSearchPageView(
                    indices,
                    await dashboardsTask,
                    "*",
                    [],
                    null,
                    (await canSyncTask).Succeeded,
                    null));

            var fields = await dispatcher.Query(
                new ListOpenSearchFieldsQuery(projectId, selectedIndex), ct);
            OpenSearchLastUpdatedView? lastUpdated = null;
            var dateFields = fields
                .Where(field => field.Aggregatable && field.Type is "date" or "date_nanos")
                .OrderBy(field => LastUpdatedFieldPriority(field.Name))
                .ThenBy(field => field.Name)
                .Select(field => field.Name)
                .ToArray();
            if (dateFields.Length > 0)
            {
                try
                {
                    lastUpdated = await dispatcher.Query(
                        new GetOpenSearchLastUpdatedQuery(projectId, selectedIndex, dateFields), ct);
                }
                catch (InvalidOperationException)
                {
                    // Optional metadata must not prevent field browsing or search.
                }
            }

            return Ok(new OpenSearchPageView(
                indices,
                await dashboardsTask,
                selectedIndex,
                fields,
                lastUpdated,
                (await canSyncTask).Succeeded,
                null));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Ok(new OpenSearchPageView(
                [],
                await dashboardsTask,
                index ?? "*",
                [],
                null,
                (await canSyncTask).Succeeded,
                exception.Message));
        }
    }

    [HttpGet("indices")]
    public async Task<IActionResult> Indices(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListOpenSearchIndicesQuery(projectId), ct));

    [HttpGet("fields")]
    public async Task<IActionResult> Fields(
        Guid projectId,
        [FromQuery] string index,
        CancellationToken ct)
        => Ok(await dispatcher.Query(new ListOpenSearchFieldsQuery(projectId, index), ct));

    [HttpPost("search")]
    public async Task<IActionResult> Search(
        Guid projectId,
        [FromBody] OpenSearchProxySearchRequest request,
        CancellationToken ct)
        => Ok(await dispatcher.Query(new SearchOpenSearchQuery(new OpenSearchSearchRequest(
            projectId,
            request.IndexPattern,
            request.QueryText,
            request.Page,
            request.PageSize,
            request.BucketField,
            request.BucketType,
            request.ChartType,
            request.MetricType,
            request.MetricField,
            request.DateInterval)), ct));

    [HttpPost("dashboards")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> SaveDashboard(
        Guid projectId,
        [FromBody] SaveOpenSearchDashboardRequest request,
        CancellationToken ct)
        => Ok(await dispatcher.Send(new SaveOpenSearchDashboardCommand(
            projectId,
            request.Name,
            request.IndexPattern,
            request.QueryText,
            request.BucketField,
            request.BucketType,
            request.ChartType,
            request.MetricType,
            request.MetricField,
            request.DateInterval,
            request.ChartSpecJson,
            request.DashboardId), ct));

    [HttpDelete("dashboards/{dashboardId:guid}")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> DeleteDashboard(
        Guid projectId,
        Guid dashboardId,
        CancellationToken ct)
    {
        _ = projectId;
        return await dispatcher.Send(new DeleteOpenSearchDashboardCommand(dashboardId), ct)
            ? NoContent()
            : NotFound(new { error = "The dashboard does not exist." });
    }

    [HttpPost("sync")]
    [Authorize(Policy = Permission.SettingsManage)]
    public async Task<IActionResult> Sync(Guid projectId, CancellationToken ct)
        => Accepted(await dispatcher.Send(new TriggerOpenSearchSyncCommand(projectId), ct));

    private static string? SelectIndex(
        IReadOnlyList<OpenSearchIndexView> indices,
        string? requested)
        => !string.IsNullOrWhiteSpace(requested)
            && indices.Any(item => string.Equals(item.Name, requested, StringComparison.Ordinal))
                ? requested
                : indices.FirstOrDefault(item =>
                    item.DocumentCount > 0
                    && item.Name.Contains(
                        "property-feasibility-assessments",
                        StringComparison.OrdinalIgnoreCase))?.Name
                    ?? indices.FirstOrDefault(item =>
                        item.DocumentCount > 0
                        && item.Name.Contains(
                            "development-applications",
                            StringComparison.OrdinalIgnoreCase))?.Name
                    ?? indices.OrderByDescending(item => item.DocumentCount).FirstOrDefault()?.Name;

    private static int LastUpdatedFieldPriority(string field)
    {
        var name = field.ToLowerInvariant();
        if (name.Contains("updated") || name.Contains("last_modified")) return 0;
        if (name.Contains("modified")) return 1;
        if (name.Contains("timestamp")) return 2;
        if (name.Contains("created")) return 3;
        return 4;
    }
}
