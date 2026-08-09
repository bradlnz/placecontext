using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Contracts.Api;

namespace PlaceContext.Data.Controllers;

[ApiController]
[Route("api/data/internal/projects/{projectId:guid}/rows")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public sealed class InternalRowsController(IProjectDataStore data) : ControllerBase
{
    [HttpGet("tables")]
    public async Task<IActionResult> ListTables(Guid projectId, CancellationToken ct)
        => Ok(await data.ListTablesAsync(projectId, ct));

    [HttpGet("tables/{tableName}/page")]
    public async Task<IActionResult> QueryTable(
        Guid projectId,
        string tableName,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await data.QueryTablePageAsync(
            projectId,
            tableName,
            search,
            Math.Max(1, page),
            Math.Clamp(pageSize, 1, 500),
            ct: ct));

    [HttpPost]
    public async Task<IActionResult> Insert(
        Guid projectId,
        InternalInsertRowRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { error = "Table name is required." });
        await data.InsertRowAsync(projectId, request.TableName, request.Values, ct);
        return Accepted();
    }
}
