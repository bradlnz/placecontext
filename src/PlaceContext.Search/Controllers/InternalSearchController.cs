using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Search.Controllers;

[ApiController]
[Route("api/search/internal")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public sealed class InternalSearchController(
    IDispatcher dispatcher,
    IOpenSearchDataGateway gateway,
    IOpenSearchConnectionResolver connectionResolver) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/run-outputs")]
    public async Task<IActionResult> SearchRunOutputs(
        Guid projectId,
        [FromQuery] string term,
        [FromQuery] int take = 10,
        CancellationToken ct = default)
        => Ok(await dispatcher.Query(
            new SearchRunOutputsQuery(projectId, term, Math.Clamp(take, 1, 100)), ct));

    [HttpGet("projects/{projectId:guid}/job-environment")]
    public async Task<IActionResult> JobEnvironment(Guid projectId, CancellationToken ct)
        => Ok(await connectionResolver.GetJobEnvironmentAsync(projectId, ct));

    [HttpGet("projects/{projectId:guid}/indices")]
    public async Task<IActionResult> Indices(Guid projectId, CancellationToken ct)
        => Ok(await gateway.ListIndicesAsync(projectId, ct));

    [HttpPost("projects/{projectId:guid}/sql")]
    public async Task<IActionResult> Sql(Guid projectId, DataSearchSqlRequest request, CancellationToken ct)
        => Ok(await gateway.SearchSqlAsync(projectId, request.Sql, ct));

    [HttpPut("projects/{projectId:guid}/indices")]
    public async Task<IActionResult> ReplaceIndex(
        Guid projectId,
        ReplaceDataSearchIndexRequest request,
        CancellationToken ct)
    {
        await gateway.DeleteIndexAsync(projectId, request.IndexName, ct);
        await gateway.CreateIndexAsync(
            projectId,
            request.IndexName,
            request.MappingFields.Select(field => new OpenSearchMappingField(field.Name, field.OpenSearchType)).ToList(),
            ct);
        var indexed = await gateway.IndexBulkAsync(
            projectId,
            request.IndexName,
            request.ColumnNames,
            request.Rows,
            ct,
            request.JsonColumnNames);
        return Ok(new { indexed });
    }
}
