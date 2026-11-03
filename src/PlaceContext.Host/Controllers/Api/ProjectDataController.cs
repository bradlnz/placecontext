using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Access;
using PlaceContext.Host.Api;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers.Api;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/data")]
[Authorize(AuthenticationSchemes = UserApiTokenAuthenticationHandler.SchemeName, Policy = Permission.DataRead)]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProjectDataController(PlaceContextService service) : ControllerBase
{
    [HttpGet("tables")]
    public async Task<IActionResult> Tables(Guid projectId)
    {
        var tables = await service.ListProjectDataTablesAsync(projectId, HttpContext.RequestAborted);
        return Ok(tables.Select(table => new ProjectDataTableResponse(
            table.Name, table.RowEstimate, table.IsView, table.ReadOnly)).ToArray());
    }

    [HttpPost("query")]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Query(Guid projectId, ProjectDataQueryRequest request)
    {
        try
        {
            var result = await service.QueryProjectDataAsync(projectId, request.Sql, HttpContext.RequestAborted);
            return Ok(new ProjectDataQueryResponse(result.Columns, result.Rows, result.Truncated));
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Provide a read-only SQL query of 1–32768 characters." });
        }
        catch (DbException)
        {
            // Database diagnostics can contain source values; keep them out of API responses.
            return BadRequest(new { error = "Query could not be completed. Check the SQL, table names and query limits." });
        }
    }
}
