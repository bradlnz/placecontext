using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Contracts.Api;
using PlaceContext.Data.Integration;

namespace PlaceContext.Data.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/data-studio")]
[Authorize(Policy = Permission.DataRead)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ProjectDataStudioController(IDispatcher dispatcher, IDataSearchClient search) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ProjectDataStudioResponse>> Get(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var tablesTask = dispatcher.Query(new ListProjectDataTablesQuery(projectId), cancellationToken);
        var indicesTask = search.ListIndicesAsync(projectId, cancellationToken);
        var queriesTask = dispatcher.Query(new ListSavedQueriesQuery(projectId), cancellationToken);
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
                ? await search.QueryAsync(projectId, request.Sql, cancellationToken)
                : string.Equals(request.Source, "postgres", StringComparison.OrdinalIgnoreCase)
                    ? await dispatcher.Send(new ExecuteProjectDataCommand(projectId, request.Sql), cancellationToken)
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
        return Ok(await dispatcher.Send(new SaveSavedQueryCommand(
            projectId, request.Name.Trim(), request.Sql), cancellationToken));
    }

    [HttpDelete("saved-queries/{queryId:guid}")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> DeleteQuery(
        Guid projectId,
        Guid queryId,
        CancellationToken cancellationToken)
    {
        _ = projectId;
        return await dispatcher.Send(new DeleteSavedQueryCommand(queryId), cancellationToken)
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
        await dispatcher.Send(new CreateProjectTableCommand(
            projectId, request.Name.Trim(), request.Columns), cancellationToken);
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
        return Ok(await dispatcher.Send(new MaterializeTableIndexCommand(
            projectId, request.TableName, request.IndexName), cancellationToken));
    }

    [HttpPost("row-links")]
    public async Task<ActionResult<IReadOnlyList<RecordLink>>> RowLinks(
        Guid projectId,
        ProjectDataRowLinksRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { error = "A table name is required." });
        return Ok(await dispatcher.Query(new RelatedRecordLinksForRowQuery(
            projectId, request.TableName, request.Values), cancellationToken));
}
}
