using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Host.Api;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers.Api;

[ApiController]
[Route("api/v1/job-data")]
[Authorize(AuthenticationSchemes = JobDataAuthenticationHandler.SchemeName)]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class JobDataController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost("sync-source")]
    public async Task<IActionResult> SyncSource()
    {
        try
        {
            return Ok(await dispatcher.Send(new SyncJobDataSourceCommand(), HttpContext.RequestAborted));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "Source sync failed. Check source configuration and connectivity." });
        }
    }

    [HttpGet("sources/{sourceId:guid}/records")]
    public async Task<IActionResult> SourceRecords(Guid sourceId, int offset = 0, int take = 50)
    {
        try
        {
            return Ok(await dispatcher.Query(new ReadJobSourceRecordsQuery(sourceId, offset, take), HttpContext.RequestAborted));
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Use a non-negative offset and take from 1 to 100." });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost("source-records")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> WriteSourceRecords(ReceiveDataFeedRequest request)
    {
        if (request.Records is null || request.Records.Length > 100 || request.Records.Any(record => record.ValueKind != System.Text.Json.JsonValueKind.Object || !record.TryGetProperty("id", out _)))
        {
            return BadRequest(new { error = "Send up to 100 objects with an id field." });
        }
        try
        {
            return Ok(await dispatcher.Send(new WriteJobSourceRecordsCommand(request.Records.Select(record =>
                new PlaceContext.Application.DataSources.SourceRecord(record.GetProperty("id").ToString(), record.GetRawText())).ToArray(), request.DeletedKeys ?? []), HttpContext.RequestAborted));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Invalid source records." });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "Feed write failed. Check source grant, limits and embedding configuration; retry conflicts." });
        }
    }

    [HttpGet("tables")]
    public async Task<IActionResult> Tables()
    {
        var tables = await dispatcher.Query(new ListJobDataTablesQuery(), HttpContext.RequestAborted);
        return Ok(tables.Select(table => new ProjectDataTableResponse(
            table.Name, table.RowEstimate, table.IsView, table.ReadOnly)).ToArray());
    }

    [HttpPost("query")]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Query(ProjectDataQueryRequest request)
    {
        try
        {
            var result = await dispatcher.Query(new QueryJobDataQuery(request.Sql), HttpContext.RequestAborted);
            return Ok(new ProjectDataQueryResponse(result.Columns, result.Rows, result.Truncated));
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Provide a read-only SQL query of 1–32768 characters." });
        }
        catch (DbException)
        {
            return BadRequest(new { error = "Query could not be completed. Check the SQL, table names and query limits." });
        }
    }
}
