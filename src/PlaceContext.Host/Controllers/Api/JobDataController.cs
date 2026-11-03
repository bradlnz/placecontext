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
