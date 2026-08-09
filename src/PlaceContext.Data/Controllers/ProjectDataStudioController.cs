using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Contracts.Api;

namespace PlaceContext.Data.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/data-studio")]
[Authorize(Policy = Permission.DataRead)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ProjectDataStudioController(IPlaceContextService placeContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ProjectDataStudioResponse>> Get(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var tablesTask = placeContext.ListProjectDataTablesAsync(projectId, cancellationToken);
        var indicesTask = placeContext.ListOpenSearchIndicesAsync(projectId, cancellationToken);
        var queriesTask = placeContext.ListSavedQueriesAsync(projectId, cancellationToken);
        await Task.WhenAll(tablesTask, indicesTask, queriesTask);
        return Ok(new ProjectDataStudioResponse(
            await tablesTask,
            (await indicesTask)
                .Select(index => new DataStudioIndexResponse(
                    index.Name,
                    index.DocumentCount,
                    index.StoreSize))
                .ToArray(),
            await queriesTask));
    }

    [HttpPost("queries/run")]
    public async Task<ActionResult<ProjectQueryResult>> RunQuery(
        Guid projectId,
        RunProjectDataQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sql))
            return BadRequest(new { error = "SQL is required." });

        try
        {
            var result = string.Equals(request.Source, "opensearch", StringComparison.OrdinalIgnoreCase)
                ? await placeContext.SearchOpenSearchSqlAsync(projectId, request.Sql, cancellationToken)
                : string.Equals(request.Source, "postgres", StringComparison.OrdinalIgnoreCase)
                    ? await placeContext.ExecuteProjectDataAsync(projectId, request.Sql, cancellationToken)
                    : null;
            return result is null
                ? BadRequest(new { error = "Source must be postgres or opensearch." })
                : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("saved-queries")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<ActionResult<SavedQueryRecord>> SaveQuery(
        Guid projectId,
        SaveProjectDataQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Sql))
            return BadRequest(new { error = "A name and SQL are required." });
        return Ok(await placeContext.SaveSavedQueryAsync(
            projectId,
            request.Name.Trim(),
            request.Sql,
            cancellationToken));
    }

    [HttpDelete("saved-queries/{queryId:guid}")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> DeleteQuery(
        Guid projectId,
        Guid queryId,
        CancellationToken cancellationToken)
    {
        _ = projectId;
        return await placeContext.DeleteSavedQueryAsync(queryId, cancellationToken)
            ? NoContent()
            : NotFound(new { error = "The saved query does not exist." });
    }

    [HttpPost("tables")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> CreateTable(
        Guid projectId,
        CreateProjectDataTableRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Columns.Count == 0)
            return BadRequest(new { error = "A table name and at least one column are required." });
        await placeContext.CreateProjectTableAsync(
            projectId,
            request.Name.Trim(),
            request.Columns,
            cancellationToken);
        return NoContent();
    }

    [HttpPost("materializations")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<ActionResult<MaterializeTableIndexResult>> Materialize(
        Guid projectId,
        MaterializeProjectDataTableRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { error = "A source table is required." });
        return Ok(await placeContext.MaterializeTableIndexAsync(
            projectId,
            request.TableName,
            request.IndexName,
            cancellationToken));
    }

    [HttpPost("row-links")]
    public async Task<ActionResult<IReadOnlyList<RecordLink>>> RowLinks(
        Guid projectId,
        ProjectDataRowLinksRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { error = "A table name is required." });
        return Ok(await placeContext.RelatedRecordLinksForRowAsync(
            projectId,
            request.TableName,
            request.Values,
            cancellationToken));
    }
}
